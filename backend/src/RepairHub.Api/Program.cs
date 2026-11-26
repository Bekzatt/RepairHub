using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using RepairHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Provided via environment variable ConnectionStrings__Default (Docker, CI, production)
// or user secrets in local development. Never stored in appsettings files.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Default' is not configured. Set the ConnectionStrings__Default environment variable.");
}

builder.Services.AddInfrastructure(connectionString);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// One compact log entry per request; headers and bodies are not logged.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

var app = builder.Build();

// Unhandled exceptions become RFC 7807 problem responses without internal details
// (in Development the developer exception page is shown instead).
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "RepairHub API v1");
        options.DocumentTitle = "RepairHub API";
    });
}

// Liveness: the process is running. No dependencies are checked.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: the app can serve requests (database reachable).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(DependencyInjection.ReadinessTag),
});

app.Run();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
