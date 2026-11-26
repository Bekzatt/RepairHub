using System.Net;

namespace RepairHub.Tests.Api;

public sealed class ApiPipelineTests
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task OpenApiDocument_IsServed_InDevelopment()
    {
        await using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SwaggerUi_IsNotExposed_InProduction()
    {
        await using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails404()
    {
        await using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetails500_WithoutInternalDetails()
    {
        await using var factory = new ApiFactory("Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ApiFactory.ThrowingPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ApiFactory.SensitiveMessage, body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task Startup_Fails_WhenConnectionStringIsMissing()
    {
        await using var factory = new ApiFactory { ConnectionString = null };

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("ConnectionStrings__Default", Flatten(exception));
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : exception.Message + " | " + Flatten(exception.InnerException);
}
