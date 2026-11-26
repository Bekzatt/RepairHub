using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RepairHub.Application.Abstractions;
using RepairHub.Domain.Users;

namespace RepairHub.Infrastructure.Persistence;

/// <summary>
/// Used only by the `dotnet ef` tooling so migrations can be created without starting the API.
/// `migrations add` never connects; `database update` uses ConnectionStrings__Default when set.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=repairhub_design_time";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, new NoCurrentUser());
    }

    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public Guid? WorkshopId => null;
        public UserRole? Role => null;
    }
}
