using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Domain.Users;

namespace RepairHub.Application.Auth;

/// <summary>Login, refresh-token rotation, logout and password change.</summary>
public sealed class AuthService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    TimeProvider time,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger)
{
    private static string? _dummyHash;

    public async Task<AuthSession> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);

        // Tenant filter bypass (allow-listed): the workshop is not known before login.
        var user = await db.Users.AcrossAllWorkshops().SingleOrDefaultAsync(u => u.NormalizedEmail == email, ct);

        if (user is null)
        {
            // Spend the same time as a real check so response timing does not reveal which emails exist.
            passwordHasher.Verify(_dummyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString()), request.Password);
            throw new AuthenticationFailedException();
        }

        var check = passwordHasher.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed || !user.IsActive)
        {
            logger.LogInformation("Failed sign-in for user {UserId}", user.Id);
            throw new AuthenticationFailedException();
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
            user.UpgradePasswordHash(passwordHasher.Hash(request.Password));

        var session = await IssueSessionAsync(user, familyId: Guid.CreateVersion7(), ct);
        logger.LogInformation("User {UserId} signed in", user.Id);
        return session;
    }

    public async Task<AuthSession> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 128)
            throw new AuthenticationFailedException();

        var now = time.GetUtcNow();
        var tokenHash = RefreshTokenGenerator.Hash(refreshToken);
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (token is null)
            throw new AuthenticationFailedException();

        if (token.RevokedAt is { } revokedAt)
        {
            // Only a token that was ROTATED (has a successor) indicates a copy in someone else's hands.
            // Tokens revoked by logout, password change or deactivation are simply rejected.
            var wasRotated = token.ReplacedById is not null;
            if (wasRotated && now - revokedAt > TimeSpan.FromSeconds(options.Value.RefreshReuseGraceSeconds))
                await HandleTokenReuseAsync(token, now, ct);
            throw new AuthenticationFailedException();
        }

        if (token.IsExpired(now))
            throw new AuthenticationFailedException();

        // Tenant filter bypass (allow-listed): the refresh token identifies the user; no access token is present.
        var user = await db.Users.AcrossAllWorkshops().SingleOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || !user.IsActive)
            throw new AuthenticationFailedException();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Atomically consume the token. If a concurrent request already rotated it, nothing is updated.
        var replacementId = Guid.CreateVersion7(now);
        var consumed = await db.RefreshTokens
            .Where(t => t.Id == token.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, now)
                .SetProperty(t => t.ReplacedById, replacementId), ct);
        if (consumed == 0)
            throw new AuthenticationFailedException();

        var session = await IssueSessionAsync(user, token.FamilyId, ct, replacementId);
        await transaction.CommitAsync(ct);
        return session;
    }

    /// <summary>Revokes the whole login session (token family) of the presented refresh token.</summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 128)
            return;

        var tokenHash = RefreshTokenGenerator.Hash(refreshToken);
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId is { } id)
            await RevokeFamilyAsync(id, time.GetUtcNow(), ct);
    }

    /// <summary>
    /// Changes the caller's password, signs out all other sessions and returns a fresh session for this device.
    /// </summary>
    public async Task<AuthSession> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await GetCurrentUserEntityAsync(ct);

        if (passwordHasher.Verify(user.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
            throw new ValidationFailedException("currentPassword", "Current password is incorrect.");
        if (request.NewPassword == request.CurrentPassword)
            throw new ValidationFailedException("newPassword", "New password must differ from the current one.");
        PasswordPolicy.EnsureValid(request.NewPassword, user.Email, "newPassword");

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        user.SetPasswordHash(passwordHasher.Hash(request.NewPassword), now);
        await RevokeAllForUserAsync(user.Id, now, ct);
        var session = await IssueSessionAsync(user, Guid.CreateVersion7(now), ct);

        await transaction.CommitAsync(ct);
        logger.LogInformation("User {UserId} changed their password", user.Id);
        return session;
    }

    public async Task<CurrentUserDto> GetMeAsync(CancellationToken ct)
    {
        var user = await GetCurrentUserEntityAsync(ct);
        return await ToCurrentUserDtoAsync(user, ct);
    }

    /// <summary>Revokes every refresh token of a user (deactivation, password reset).</summary>
    public static Task RevokeAllForUserAsync(IAppDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    private Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct) =>
        RevokeAllForUserAsync(db, userId, now, ct);

    private Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

    /// <summary>
    /// A rotated token was presented again: someone holds a copy. Revoke the whole family and
    /// invalidate the user's access tokens so neither the thief nor the victim keeps a session.
    /// </summary>
    private async Task HandleTokenReuseAsync(RefreshToken token, DateTimeOffset now, CancellationToken ct)
    {
        logger.LogWarning(
            "Refresh token reuse detected for user {UserId}, family {FamilyId}; revoking the session",
            token.UserId, token.FamilyId);

        await RevokeFamilyAsync(token.FamilyId, now, ct);

        // Tenant filter bypass (allow-listed): anonymous request, user identified by the token.
        var user = await db.Users.AcrossAllWorkshops().SingleOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is not null)
        {
            user.InvalidateSessions(now);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<AuthSession> IssueSessionAsync(
        User user, Guid familyId, CancellationToken ct, Guid? refreshTokenId = null)
    {
        var now = time.GetUtcNow();
        var lifetime = TimeSpan.FromDays(options.Value.RefreshTokenDays);
        var rawToken = RefreshTokenGenerator.NewToken();

        var refreshToken = RefreshToken.Issue(user.Id, familyId, RefreshTokenGenerator.Hash(rawToken), now, lifetime, refreshTokenId);
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(ct);

        var accessToken = accessTokens.Issue(user);
        var me = await ToCurrentUserDtoAsync(user, ct);
        return new AuthSession(
            new AuthResponse(accessToken.Token, accessToken.ExpiresAt, me), rawToken, refreshToken.ExpiresAt);
    }

    private async Task<User> GetCurrentUserEntityAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new AuthenticationFailedException();
        return await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AuthenticationFailedException();
    }

    private async Task<CurrentUserDto> ToCurrentUserDtoAsync(User user, CancellationToken ct)
    {
        // Tenant filter bypass (allow-listed): during login/refresh there is no current workshop yet;
        // the query is pinned to the user's own workshop id.
        var workshop = await db.Workshops.AcrossAllWorkshops()
            .Where(w => w.Id == user.WorkshopId)
            .Select(w => new WorkshopSummaryDto(w.Id, w.Name, w.Currency, w.TimeZone))
            .SingleAsync(ct);

        return new CurrentUserDto(user.Id, user.FullName, user.Email, user.Phone, user.Role, workshop);
    }
}
