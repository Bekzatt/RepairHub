using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace RepairHub.Tests;

/// <summary>
/// Hosts the real API in memory without a database. The connection string points to a closed port,
/// so these tests never need (or touch) a real database.
/// </summary>
public sealed class ApiFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=repairhub_test;Username=test;Timeout=1";

    public const string ThrowingPath = "/__tests/throw";
    public const string SensitiveMessage = "internal detail that must not leak";

    public string? ConnectionString { get; init; } = UnreachableDatabase;
    public string? SigningKey { get; init; } = "unit-tests-signing-key-0123456789abcdef";
    public Dictionary<string, string> Settings { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", ConnectionString ?? string.Empty);
        builder.UseSetting("Jwt:SigningKey", SigningKey ?? string.Empty);
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        // Adds the test-only ThrowingController below to the real pipeline.
        builder.ConfigureServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly));
    }
}

/// <summary>Test-only endpoint that always throws, to verify the production error pipeline.</summary>
[ApiController]
[AllowAnonymous]
[Route(ApiFactory.ThrowingPath)]
public sealed class ThrowingController : ControllerBase
{
    [HttpGet]
    public IActionResult Throw() => throw new InvalidOperationException(ApiFactory.SensitiveMessage);
}
