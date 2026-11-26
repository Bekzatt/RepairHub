using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace RepairHub.Tests;

/// <summary>
/// Hosts the real API in memory. The connection string points to a closed port,
/// so tests never need (or touch) a real database.
/// </summary>
public sealed class ApiFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=repairhub_test;Username=test;Timeout=1";

    public const string ThrowingPath = "/__tests/throw";
    public const string SensitiveMessage = "internal detail that must not leak";

    public string? ConnectionString { get; init; } = UnreachableDatabase;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", ConnectionString ?? string.Empty);
        builder.ConfigureServices(services =>
            services.AddTransient<IStartupFilter, ThrowingEndpointStartupFilter>());
    }

    /// <summary>Appends a test-only endpoint after the app's own pipeline that always throws.</summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map(ThrowingPath, branch =>
                branch.Run(_ => throw new InvalidOperationException(SensitiveMessage)));
        };
    }
}
