using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using RepairHub.Application.Auth;
using RepairHub.Domain.Users;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Auth;

[Collection(PostgresCollection.Name)]
public sealed class AuthenticationTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessTokenAndHardenedRefreshCookie()
    {
        var workshop = await Api.CreateWorkshopAsync();
        using var client = Api.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = (await MeAsync(workshop.Owner)).Email.ToUpperInvariant(), password = TestApi.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<AuthResponse>();
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.Equal(UserRole.Owner, body.User.Role);
        Assert.Equal(workshop.WorkshopId, body.User.Workshop.Id);
        Assert.DoesNotContain("refresh", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var cookie = response.GetRefreshCookieHeader()!.ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/auth", cookie);
        Assert.Contains("secure", cookie); // Production configuration
    }

    [Theory]
    [InlineData("wrong-password-123")]
    [InlineData("")]
    public async Task Login_WithWrongPassword_Returns401(string password)
    {
        var workshop = await Api.CreateWorkshopAsync();
        var me = await MeAsync(workshop.Owner);
        using var client = Api.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = me.Email, password });

        Assert.Equal(password.Length == 0 ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.GetRefreshCookie());
    }

    [Fact]
    public async Task Login_UnknownEmailAndInactiveUser_AreIndistinguishableFromWrongPassword()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var technician = await Api.AddUserAsync(workshop.Owner, UserRole.Technician);
        var technicianEmail = (await MeAsync(technician)).Email;
        Assert.Equal(HttpStatusCode.OK,
            (await Api.PostAsAsync(workshop.Owner, $"/api/users/{technician.UserId}/deactivate")).StatusCode);

        using var client = Api.CreateApiClient();
        var unknown = await client.PostAsJsonAsync("/api/auth/login",
            new { email = TestApi.UniqueEmail(), password = TestApi.Password });
        var inactive = await client.PostAsJsonAsync("/api/auth/login",
            new { email = technicianEmail, password = TestApi.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        Assert.Equal("invalid_credentials", await unknown.ReadErrorCodeAsync());
        Assert.Equal("invalid_credentials", await inactive.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Login_WithInvalidEmailFormat_Returns400WithFieldError()
    {
        using var client = Api.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "not-an-email", password = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"email\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        using var client = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/workshop")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task ExpiredAccessToken_IsRejected()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var token = CreateToken(workshop.Owner, IntegrationApiFactory.SigningKey, expires: DateTime.UtcNow.AddMinutes(-5));

        using var client = Api.CreateApiClient().WithToken(token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task AccessToken_SignedWithAnotherKey_IsRejected()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var forged = CreateToken(workshop.Owner, "attacker-controlled-key-0123456789abcdef", DateTime.UtcNow.AddMinutes(5));

        using var client = Api.CreateApiClient().WithToken(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndOldTokenStopsWorking()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var original = workshop.Owner.RefreshCookie;

        var refreshed = await Api.RefreshAsync(original);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var rotated = refreshed.GetRefreshCookie();
        Assert.NotNull(rotated);
        Assert.NotEqual(original, rotated);

        var body = await refreshed.ReadAsync<AuthResponse>();
        using var client = Api.CreateApiClient().WithToken(body!.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.RefreshAsync(original)).StatusCode);
    }

    [Fact]
    public async Task Refresh_ReuseOfRotatedToken_RevokesWholeSessionAndAccessTokens()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var stolen = workshop.Owner.RefreshCookie;

        // The legitimate client rotates the token...
        var legit = await Api.RefreshAsync(stolen);
        var legitCookie = legit.GetRefreshCookie()!;
        var legitAccessToken = (await legit.ReadAsync<AuthResponse>())!.AccessToken;

        // ...then an attacker replays the old one: reuse detected.
        var replay = await Api.RefreshAsync(stolen);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The whole family is revoked: the newest refresh token no longer works either...
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.RefreshAsync(legitCookie)).StatusCode);
        // ...and access tokens issued before the incident are rejected immediately.
        using var client = Api.CreateApiClient().WithToken(legitAccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        // The user can still sign in again with their password.
        await Api.LoginAsync(EmailOf(workshop), TestApi.Password);
    }

    [Fact]
    public async Task Refresh_WithLoggedOutToken_IsRejected_WithoutSigningOutOtherDevices()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var otherDevice = await Api.LoginAsync(EmailOf(workshop), TestApi.Password);

        using var client = Api.CreateApiClient();
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Add("X-Requested-With", "RepairHub");
        logout.Headers.Add("Cookie", $"{TestApi.CookieName}={workshop.Owner.RefreshCookie}");
        await client.SendAsync(logout);

        // Replaying a token that was revoked by logout (not rotated) is not treated as theft...
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.RefreshAsync(workshop.Owner.RefreshCookie)).StatusCode);

        // ...so the user's other device keeps working.
        Assert.Equal(HttpStatusCode.OK, (await Api.GetAsAsync(otherDevice, "/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.RefreshAsync(otherDevice.RefreshCookie)).StatusCode);
    }

    [Fact]
    public async Task PasswordChange_ThenOldDeviceRefreshes_DoesNotSignOutTheDeviceThatChangedIt()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var oldDevice = await Api.LoginAsync(EmailOf(workshop), TestApi.Password);

        var changed = await Api.PostAsAsync(workshop.Owner, "/api/auth/change-password",
            new { currentPassword = TestApi.Password, newPassword = "brand-new-password-2" });
        var newSession = await changed.ReadAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.RefreshAsync(oldDevice.RefreshCookie)).StatusCode);

        using var client = Api.CreateApiClient().WithToken(newSession!.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutClientHeader_IsRejected()
    {
        var workshop = await Api.CreateWorkshopAsync();
        using var client = Api.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"{TestApi.CookieName}={workshop.Owner.RefreshCookie}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        // The token was not consumed.
        Assert.Equal(HttpStatusCode.OK, (await Api.RefreshAsync(workshop.Owner.RefreshCookie)).StatusCode);
    }

    [Fact]
    public async Task Refresh_WithGarbageToken_Returns401AndClearsCookie()
    {
        var response = await Api.RefreshAsync("not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull(response.GetRefreshCookieHeader()); // deletion cookie
        Assert.Null(response.GetRefreshCookie());
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        var workshop = await Api.CreateWorkshopAsync();
        using var client = Api.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-Requested-With", "RepairHub");
        request.Headers.Add("Cookie", $"{TestApi.CookieName}={workshop.Owner.RefreshCookie}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(response.GetRefreshCookie());
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.RefreshAsync(workshop.Owner.RefreshCookie)).StatusCode);
    }

    [Fact]
    public async Task DeactivatedUser_AccessAndRefreshTokensAreRejectedImmediately()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var technician = await Api.AddUserAsync(workshop.Owner, UserRole.Technician);
        Assert.Equal(HttpStatusCode.OK, (await Api.GetAsAsync(technician, "/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await Api.PostAsAsync(workshop.Owner, $"/api/users/{technician.UserId}/deactivate")).StatusCode);

        // The access token has not expired, but it is rejected because the user is inactive.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsAsync(technician, "/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.RefreshAsync(technician.RefreshCookie)).StatusCode);
    }

    [Fact]
    public async Task RoleChange_InvalidatesOutstandingAccessTokens()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var admin = await Api.AddUserAsync(workshop.Owner, UserRole.Admin);

        var demote = await Api.PutAsAsync(workshop.Owner, $"/api/users/{admin.UserId}",
            new { fullName = "Demoted", role = "Technician" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);

        // The old token still says "Admin" — it must not be accepted any more.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsAsync(admin, "/api/users")).StatusCode);

        // A refreshed token carries the new role.
        var refreshed = await (await Api.RefreshAsync(admin.RefreshCookie)).ReadAsync<AuthResponse>();
        Assert.Equal(UserRole.Technician, refreshed!.User.Role);
    }

    [Fact]
    public async Task ChangePassword_ValidatesAndSignsOutOtherSessions()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var email = EmailOf(workshop);
        var otherDevice = await Api.LoginAsync(email, TestApi.Password);

        var wrongCurrent = await Api.PostAsAsync(workshop.Owner, "/api/auth/change-password",
            new { currentPassword = "not-my-password", newPassword = "brand-new-password-1" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        var weak = await Api.PostAsAsync(workshop.Owner, "/api/auth/change-password",
            new { currentPassword = TestApi.Password, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("\"newPassword\"", await weak.Content.ReadAsStringAsync());

        var changed = await Api.PostAsAsync(workshop.Owner, "/api/auth/change-password",
            new { currentPassword = TestApi.Password, newPassword = "brand-new-password-1" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotNull(changed.GetRefreshCookie());

        // Other sessions are gone; old password no longer works; new one does.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.RefreshAsync(otherDevice.RefreshCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.GetAsAsync(otherDevice, "/api/auth/me")).StatusCode);
        using var client = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new { email, password = TestApi.Password })).StatusCode);
        await Api.LoginAsync(email, "brand-new-password-1");
    }

    private async Task<CurrentUserDto> MeAsync(SignedIn user) =>
        (await (await Api.GetAsAsync(user, "/api/auth/me")).ReadAsync<CurrentUserDto>())!;

    private static string EmailOf(TestWorkshop workshop) => workshop.Owner.Auth.User.Email;

    private static string CreateToken(SignedIn user, string key, DateTime expires) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "repairhub",
            Audience = "repairhub",
            IssuedAt = expires.AddMinutes(-15),
            NotBefore = expires.AddMinutes(-15),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.UserId.ToString(),
                ["wid"] = user.WorkshopId.ToString(),
                ["role"] = "Owner",
                ["tv"] = "1",
            },
        });
}
