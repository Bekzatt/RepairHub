using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RepairHub.Infrastructure.Persistence;

namespace RepairHub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Tag used to select health checks that must pass before the app receives traffic.</summary>
    public const string ReadinessTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>(name: "database", tags: [ReadinessTag]);

        return services;
    }
}
