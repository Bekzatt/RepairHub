using Microsoft.EntityFrameworkCore;

namespace RepairHub.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for the application.
/// Entity configurations will live next to it (IEntityTypeConfiguration classes)
/// and are picked up automatically from this assembly.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
