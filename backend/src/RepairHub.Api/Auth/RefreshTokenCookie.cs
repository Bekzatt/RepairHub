using Microsoft.Extensions.Options;

namespace RepairHub.Api.Auth;

/// <summary>Configuration section "Auth:RefreshCookie".</summary>
public sealed class RefreshCookieOptions
{
    public const string SectionName = "Auth:RefreshCookie";

    public string Name { get; set; } = "rh_refresh";

    /// <summary>
    /// Sent only over HTTPS. Must be true outside Development (enforced at startup).
    /// Plain-HTTP local development through the Vite proxy needs false.
    /// </summary>
    public bool Secure { get; set; } = true;
}

/// <summary>
/// Writes the refresh token as an HttpOnly, SameSite=Strict cookie scoped to /api/auth, so it is never
/// readable by JavaScript and only sent to the refresh/logout endpoints of this site.
/// </summary>
public sealed class RefreshTokenCookie(IOptions<RefreshCookieOptions> options)
{
    public const string Path = "/api/auth";

    public string? Read(HttpRequest request) => request.Cookies[options.Value.Name];

    public void Write(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(options.Value.Name, token, Build(expiresAt));

    public void Delete(HttpResponse response) =>
        response.Cookies.Delete(options.Value.Name, Build(expiresAt: null));

    private CookieOptions Build(DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = options.Value.Secure,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true,
    };
}
