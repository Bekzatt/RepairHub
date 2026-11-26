using Microsoft.EntityFrameworkCore;
using RepairHub.Application.Abstractions;

namespace RepairHub.Application.Auth;

/// <summary>
/// Called for every request carrying a validly signed access token. Rejects tokens of users that were
/// deactivated, changed role or password, or moved — i.e. whose token version no longer matches.
/// </summary>
public sealed class AccessTokenSessionValidator(IAppDbContext db)
{
    public Task<bool> IsValidAsync(Guid userId, Guid workshopId, int tokenVersion, CancellationToken ct) =>
        // Tenant filter bypass (allow-listed): runs while the token is being validated, before the request
        // has a current user. The query is pinned to the workshop id from the signed token.
        db.Users.AcrossAllWorkshops().AnyAsync(u =>
            u.Id == userId &&
            u.WorkshopId == workshopId &&
            u.IsActive &&
            u.TokenVersion == tokenVersion, ct);
}
