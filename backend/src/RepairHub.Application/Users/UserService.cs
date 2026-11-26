using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Auth;
using RepairHub.Application.Common;
using RepairHub.Domain.Users;

namespace RepairHub.Application.Users;

/// <summary>
/// Staff management within the caller's workshop.
/// <para>
/// Every mutation runs in a transaction that first locks the workshop row and only then loads the actor and
/// the target. Concurrent mutations in the same workshop are therefore serialized and always see each other's
/// committed changes, which keeps the "at least one active Owner" invariant safe under concurrency.
/// </para>
/// </summary>
public sealed class UserService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    TimeProvider time,
    ILogger<UserService> logger)
{
    public async Task<PagedResult<UserDto>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        EnsureCanView();

        var users = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            users = users.Where(u => u.FullName.ToLower().Contains(term) || u.NormalizedEmail.Contains(term));
        }
        if (query.Role is { } role) users = users.Where(u => u.Role == role);
        if (query.IsActive is { } isActive) users = users.Where(u => u.IsActive == isActive);

        var total = await users.CountAsync(ct);
        var items = await users
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new UserDto(u.Id, u.FullName, u.Email, u.Phone, u.Role, u.IsActive, u.CreatedAt, u.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<UserDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct)
    {
        EnsureCanView();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException();
        return ToDto(user);
    }

    public Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct) =>
        MutateAsync(async (actor, now) =>
        {
            var role = request.Role!.Value;
            if (!UserPermissions.CanManageRole(actor.Role, role))
                throw new ForbiddenException(ErrorCodes.RoleNotAllowed, "You cannot create users with this role.");

            PasswordPolicy.EnsureValid(request.Password, request.Email, "password");

            var user = User.Create(
                actor.WorkshopId, request.FullName, request.Email, request.Phone, role,
                passwordHasher.Hash(request.Password), now);
            db.Users.Add(user);
            return user;
        }, ct);

    public Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        MutateAsync(async (actor, now) =>
        {
            var target = await LoadTargetAsync(id, ct);
            var newRole = request.Role!.Value;

            if (actor.Id == target.Id)
            {
                if (newRole != target.Role)
                    throw new ForbiddenException(ErrorCodes.SelfModification, "You cannot change your own role.");
            }
            else
            {
                EnsureCanManage(actor, target.Role);
                if (!UserPermissions.CanManageRole(actor.Role, newRole))
                    throw new ForbiddenException(ErrorCodes.RoleNotAllowed, "You cannot assign this role.");
                if (target.Role == UserRole.Owner && newRole != UserRole.Owner && target.IsActive)
                    await EnsureAnotherActiveOwnerAsync(target, ct);
            }

            target.UpdateProfile(request.FullName, request.Phone, now);
            target.ChangeRole(newRole, now);
            return target;
        }, ct);

    public Task<UserDto> DeactivateAsync(Guid id, CancellationToken ct) =>
        MutateAsync(async (actor, now) =>
        {
            var target = await LoadTargetAsync(id, ct);
            if (actor.Id == target.Id)
                throw new ForbiddenException(ErrorCodes.SelfModification, "You cannot deactivate yourself.");
            EnsureCanManage(actor, target.Role);

            if (target.IsActive)
            {
                if (target.Role == UserRole.Owner)
                    await EnsureAnotherActiveOwnerAsync(target, ct);

                target.Deactivate(now);
                await AuthService.RevokeAllForUserAsync(db, target.Id, now, ct);
                logger.LogInformation("User {UserId} deactivated by {ActorId}", target.Id, actor.Id);
            }
            return target;
        }, ct);

    public Task<UserDto> ActivateAsync(Guid id, CancellationToken ct) =>
        MutateAsync(async (actor, now) =>
        {
            var target = await LoadTargetAsync(id, ct);
            EnsureCanManage(actor, target.Role);
            target.Activate(now);
            return target;
        }, ct);

    public Task<UserDto> ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct) =>
        MutateAsync(async (actor, now) =>
        {
            var target = await LoadTargetAsync(id, ct);
            if (actor.Id == target.Id)
                throw new ForbiddenException(ErrorCodes.SelfModification, "Use the change-password endpoint for your own account.");
            EnsureCanManage(actor, target.Role);
            PasswordPolicy.EnsureValid(request.NewPassword, target.Email, "newPassword");

            target.SetPasswordHash(passwordHasher.Hash(request.NewPassword), now);
            await AuthService.RevokeAllForUserAsync(db, target.Id, now, ct);
            logger.LogInformation("Password of user {UserId} reset by {ActorId}", target.Id, actor.Id);
            return target;
        }, ct);

    private async Task<UserDto> MutateAsync(Func<User, DateTimeOffset, Task<User>> mutation, CancellationToken ct)
    {
        EnsureCanView();
        var workshopId = currentUser.WorkshopId ?? throw new ForbiddenException();
        var now = time.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.LockWorkshopAsync(workshopId, ct);

        // Re-read the actor under the lock: their role or status may have changed since the token was issued.
        var actor = await db.Users.SingleOrDefaultAsync(u => u.Id == currentUser.UserId, ct);
        if (actor is null || !actor.IsActive || !UserPermissions.CanViewUsers(actor.Role))
            throw new ForbiddenException();

        var result = await mutation(actor, now);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DuplicateKeyException)
        {
            throw new ConflictException(ErrorCodes.EmailTaken, "A user with this email already exists.");
        }

        await transaction.CommitAsync(ct);
        return ToDto(result);
    }

    private async Task<User> LoadTargetAsync(Guid id, CancellationToken ct) =>
        // Tenant-filtered: a user of another workshop is indistinguishable from a missing one (404).
        await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException();

    private async Task EnsureAnotherActiveOwnerAsync(User target, CancellationToken ct)
    {
        var otherActiveOwners = await db.Users.CountAsync(
            u => u.Role == UserRole.Owner && u.IsActive && u.Id != target.Id, ct);
        if (otherActiveOwners == 0)
            throw new ConflictException(ErrorCodes.LastOwner, "The workshop must keep at least one active Owner.");
    }

    private void EnsureCanView()
    {
        if (!UserPermissions.CanViewUsers(currentUser.Role))
            throw new ForbiddenException();
    }

    private static void EnsureCanManage(User actor, UserRole targetRole)
    {
        if (!UserPermissions.CanManageRole(actor.Role, targetRole))
            throw new ForbiddenException(ErrorCodes.RoleNotAllowed, "You cannot manage users with this role.");
    }

    private static UserDto ToDto(User u) =>
        new(u.Id, u.FullName, u.Email, u.Phone, u.Role, u.IsActive, u.CreatedAt, u.UpdatedAt);
}
