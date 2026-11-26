using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RepairHub.Tests.Infrastructure;

/// <summary>
/// The real API on a real PostgreSQL database, running with the Production configuration
/// (so the refresh cookie must be Secure, exactly as in production).
/// </summary>
public sealed class IntegrationApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        // Reuse of a rotated refresh token is treated as theft immediately (no grace window) in tests.
        builder.UseSetting("Auth:RefreshReuseGraceSeconds", "0");
        builder.UseSetting("Auth:RateLimits:LoginPerMinute", "100000");
        builder.UseSetting("Auth:RateLimits:RefreshPerMinute", "100000");
    }

    /// <summary>A client that does NOT store cookies; tests pass the refresh cookie explicitly.</summary>
    public HttpClient CreateApiClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
}
