using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Domain.Common;
using RepairHub.Domain.Users;
using RepairHub.Domain.Workshops;

namespace RepairHub.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context. Entity configurations live in <c>Configurations/</c>.
/// <para>
/// Tenant isolation: every <see cref="ITenantOwned"/> entity and <see cref="Workshop"/> get a named
/// query filter that matches only the current user's workshop. The filter FAILS CLOSED — when there
/// is no current workshop (anonymous request, background job, CLI) it matches no rows at all.
/// </para>
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IAppDbContext
{
    private const string PostgresUniqueViolation = "23505";

    public DbSet<Workshop> Workshops => Set<Workshop>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Read by the query filters on every query execution (EF parameterizes context members).</summary>
    private Guid? CurrentWorkshopId => currentUser.WorkshopId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Workshop>().HasQueryFilter(
            TenantFilterBypass.FilterName,
            w => CurrentWorkshopId != null && w.Id == CurrentWorkshopId);

        var applyTenantFilter = typeof(AppDbContext).GetMethod(
            nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            applyTenantFilter.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantOwned
    {
        Expression<Func<T, bool>> filter = e => CurrentWorkshopId != null && e.WorkshopId == CurrentWorkshopId;
        modelBuilder.Entity<T>().HasQueryFilter(TenantFilterBypass.FilterName, filter);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation } pg)
        {
            throw new DuplicateKeyException(pg.ConstraintName, ex);
        }
    }

    public async Task LockWorkshopAsync(Guid workshopId, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        await Database.ExecuteSqlAsync(
            $"SELECT 1 FROM workshops WHERE id = {workshopId} FOR UPDATE", cancellationToken);
    }

    /// <summary>Arbitrary constant identifying the provisioning advisory lock ("REPAIR").</summary>
    private const long ProvisioningLockKey = 0x5245_5041_4952;

    public async Task LockProvisioningAsync(CancellationToken cancellationToken)
    {
        EnsureTransaction();
        await Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ProvisioningLockKey})", cancellationToken);
    }

    private void EnsureTransaction()
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Locks must be taken inside a transaction.");
    }
}
