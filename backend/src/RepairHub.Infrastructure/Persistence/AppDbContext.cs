using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Domain.Common;
using RepairHub.Domain.Customers;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;
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
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<RepairOrder> RepairOrders => Set<RepairOrder>();
    public DbSet<RepairOrderEvent> RepairOrderEvents => Set<RepairOrderEvent>();

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
        EnsureHistoryIsAppendOnly();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresUniqueViolation } pg)
        {
            throw new DuplicateKeyException(pg.ConstraintName, ex);
        }
    }

    /// <summary>Repair order history is append-only: refuse to persist any change to an existing event.</summary>
    private void EnsureHistoryIsAppendOnly()
    {
        if (ChangeTracker.Entries<RepairOrderEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Repair order history events cannot be modified or deleted.");
    }

    public async Task<int> NextRepairOrderSequenceAsync(Guid workshopId, int year, CancellationToken cancellationToken)
    {
        EnsureTransaction();
        // Atomic upsert: concurrent callers for the same workshop and year queue on the counter row and get
        // consecutive values; a rollback of the surrounding transaction also rolls back the increment.
        var values = await Database.SqlQuery<int>($"""
            INSERT INTO repair_order_counters (workshop_id, year, last_value) VALUES ({workshopId}, {year}, 1)
            ON CONFLICT (workshop_id, year) DO UPDATE SET last_value = repair_order_counters.last_value + 1
            RETURNING last_value AS "Value"
            """).ToListAsync(cancellationToken);
        return values.Single();
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
