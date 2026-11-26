using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RepairHub.Domain.Users;
using RepairHub.Domain.Workshops;

namespace RepairHub.Application.Abstractions;

/// <summary>
/// The application's view of the database. Implemented by the EF Core context in Infrastructure.
/// <para>
/// <see cref="Workshops"/> and <see cref="Users"/> are tenant-filtered: queries only ever see rows of the
/// current user's workshop, and see nothing when there is no current workshop. To read across workshops
/// use <see cref="TenantFilterBypass.AcrossAllWorkshops{T}"/> (allow-listed and covered by a test).
/// </para>
/// </summary>
public interface IAppDbContext
{
    DbSet<Workshop> Workshops { get; }
    DbSet<User> Users { get; }

    /// <summary>Not tenant-filtered; access only by token hash or user id inside the auth flow.</summary>
    DbSet<RefreshToken> RefreshTokens { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a row lock on the workshop (SELECT ... FOR UPDATE) until the current transaction ends.
    /// Serializes operations that must see a consistent set of the workshop's users,
    /// such as the "at least one active Owner" rule.
    /// </summary>
    Task LockWorkshopAsync(Guid workshopId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a transaction-scoped advisory lock that serializes workshop provisioning,
    /// so two concurrent runs of the bootstrap command cannot both pass the duplicate checks.
    /// </summary>
    Task LockProvisioningAsync(CancellationToken cancellationToken);
}
