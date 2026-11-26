using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using RepairHub.Application.Auth;
using RepairHub.Application.Users;
using RepairHub.Application.Workshops;
using RepairHub.Domain.Users;

namespace RepairHub.Tests.Infrastructure;

public sealed record SignedIn(AuthResponse Auth, string RefreshCookie, string Password)
{
    public string AccessToken => Auth.AccessToken;
    public Guid UserId => Auth.User.Id;
    public Guid WorkshopId => Auth.User.Workshop.Id;
}

public sealed record TestWorkshop(Guid WorkshopId, SignedIn Owner);

/// <summary>Helpers for driving the API in tests.</summary>
public static class TestApi
{
    public const string Password = "correct-horse-battery";
    public const string CookieName = "rh_refresh";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@test.local";

    /// <summary>Provisions a new workshop (as the bootstrap command does) and signs its Owner in.</summary>
    public static async Task<TestWorkshop> CreateWorkshopAsync(this IntegrationApiFactory api, string? name = null)
    {
        var ownerEmail = UniqueEmail("owner");
        await using (var scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<WorkshopProvisioningService>().CreateAsync(
                new CreateWorkshopRequest
                {
                    WorkshopName = name ?? "Workshop " + Guid.NewGuid().ToString("N")[..8],
                    OwnerFullName = "Owner",
                    OwnerEmail = ownerEmail,
                    OwnerPassword = Password,
                    AllowAdditionalWorkshop = true,
                },
                CancellationToken.None);
        }

        var owner = await api.LoginAsync(ownerEmail, Password);
        return new TestWorkshop(owner.WorkshopId, owner);
    }

    /// <summary>Creates a user through the API as <paramref name="actor"/> and signs them in.</summary>
    public static async Task<SignedIn> AddUserAsync(this IntegrationApiFactory api, SignedIn actor, UserRole role)
    {
        var email = UniqueEmail(role.ToString().ToLowerInvariant());
        using var client = api.CreateApiClient().As(actor);
        var response = await client.PostAsJsonAsync("/api/users",
            new { fullName = $"Test {role}", email, role = role.ToString(), password = Password }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await api.LoginAsync(email, Password);
    }

    public static async Task<SignedIn> LoginAsync(this IntegrationApiFactory api, string email, string password)
    {
        using var client = api.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new SignedIn((await response.ReadAsync<AuthResponse>())!, response.GetRefreshCookie()!, password);
    }

    public static async Task<HttpResponseMessage> RefreshAsync(this IntegrationApiFactory api, string refreshCookie)
    {
        using var client = api.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("X-Requested-With", "RepairHub");
        request.Headers.Add("Cookie", $"{CookieName}={refreshCookie}");
        return await client.SendAsync(request);
    }

    public static HttpClient As(this HttpClient client, SignedIn user) => client.WithToken(user.AccessToken);

    public static HttpClient WithToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static async Task<T?> ReadAsync<T>(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(Json);

    public static async Task<string?> ReadErrorCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Full Set-Cookie header of the refresh cookie, or null.</summary>
    public static string? GetRefreshCookieHeader(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
            : null;

    /// <summary>Value of the refresh cookie set by the response, or null.</summary>
    public static string? GetRefreshCookie(this HttpResponseMessage response)
    {
        var header = response.GetRefreshCookieHeader();
        if (header is null) return null;
        var value = header[(CookieName.Length + 1)..].Split(';')[0];
        return value.Length == 0 ? null : value;
    }

    public static Task<HttpResponseMessage> GetAsAsync(this IntegrationApiFactory api, SignedIn user, string url) =>
        api.CreateApiClient().As(user).GetAsync(url);

    public static Task<HttpResponseMessage> PostAsAsync(
        this IntegrationApiFactory api, SignedIn user, string url, object? body = null) =>
        api.CreateApiClient().As(user).PostAsJsonAsync(url, body ?? new { }, Json);

    public static Task<HttpResponseMessage> PutAsAsync(this IntegrationApiFactory api, SignedIn user, string url, object body) =>
        api.CreateApiClient().As(user).PutAsJsonAsync(url, body, Json);
}
