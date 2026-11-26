using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using RepairHub.Api.Auth;
using RepairHub.Api.Bootstrap;
using RepairHub.Api.Errors;
using RepairHub.Api.OpenApi;
using RepairHub.Application;
using RepairHub.Infrastructure;
using RepairHub.Infrastructure.Persistence;

var isCommand = args.Length > 0 && args[0] == CreateWorkshopCommand.Name;

// Command arguments are not configuration; keep them away from the command-line configuration provider.
var builder = WebApplication.CreateBuilder(isCommand ? [] : args);

// Provided via environment variable ConnectionStrings__Default (Docker, CI, production)
// or user secrets in local development. Never stored in appsettings files.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Default' is not configured. Set the ConnectionStrings__Default environment variable.");
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.AddRepairHubAuth(builder.Configuration, builder.Environment);

builder.Services
    .AddControllers(options =>
        // Validation error keys use the JSON (camelCase) property names.
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();

// One compact log entry per request; headers and bodies (passwords, tokens) are never logged.
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestMethod
        | HttpLoggingFields.RequestPath
        | HttpLoggingFields.ResponseStatusCode
        | HttpLoggingFields.Duration;
    options.CombineLogs = true;
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (isCommand)
{
    return await CreateWorkshopCommand.RunAsync(app.Services, args[1..]);
}

// Unhandled exceptions become RFC 7807 problem responses without internal details
// (in Development the developer exception page is shown instead).
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpLogging();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "RepairHub API v1");
        options.DocumentTitle = "RepairHub API";
    });
}

app.MapControllers();

// Liveness: the process is running. No dependencies are checked.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

// Readiness: the app can serve requests (database reachable).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(RepairHub.Infrastructure.DependencyInjection.ReadinessTag),
}).AllowAnonymous();

await app.RunAsync();
return 0;

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
