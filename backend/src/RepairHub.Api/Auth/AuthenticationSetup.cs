using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Auth;
using RepairHub.Domain.Users;

namespace RepairHub.Api.Auth;

public static class AuthenticationSetup
{
    public const string LoginRateLimit = "login";
    public const string RefreshRateLimit = "refresh";

    public static IServiceCollection AddRepairHubAuth(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RefreshCookieOptions>().Bind(configuration.GetSection(RefreshCookieOptions.SectionName))
            .Validate(o => o.Secure || environment.IsDevelopment(),
                "Auth:RefreshCookie:Secure must be true outside Development (refresh cookies require HTTPS).")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<RefreshTokenCookie>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthClaims.UserId,
                    RoleClaimType = AuthClaims.Role,
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = ValidateSessionAsync };
            });

        services.AddAuthorizationBuilder()
            // Every endpoint requires an authenticated user unless it opts out with [AllowAnonymous].
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build())
            .AddPolicy(AuthPolicies.ManageUsers, p => p.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.ManageWorkshop, p => p.RequireRole(nameof(UserRole.Owner)))
            .AddPolicy(AuthPolicies.FrontOffice,
                p => p.RequireRole(nameof(UserRole.Owner), nameof(UserRole.Admin), nameof(UserRole.Receptionist)));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRateLimit, ctx => PerClientIp(ctx, configuration, "Auth:RateLimits:LoginPerMinute", 10));
            options.AddPolicy(RefreshRateLimit, ctx => PerClientIp(ctx, configuration, "Auth:RateLimits:RefreshPerMinute", 60));
        });

        return services;
    }

    private static RateLimitPartition<string> PerClientIp(
        HttpContext context, IConfiguration configuration, string key, int defaultLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = configuration.GetValue(key, defaultLimit),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });

    /// <summary>
    /// Signature and lifetime are already verified here. Additionally require that the user still exists,
    /// is active and that the token version matches (deactivation / role or password change revoke tokens at once).
    /// </summary>
    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (!Guid.TryParse(principal?.FindFirst(AuthClaims.UserId)?.Value, out var userId) ||
            !Guid.TryParse(principal.FindFirst(AuthClaims.WorkshopId)?.Value, out var workshopId) ||
            !int.TryParse(principal.FindFirst(AuthClaims.TokenVersion)?.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var tokenVersion))
        {
            context.Fail("Malformed access token.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<AccessTokenSessionValidator>();
        if (!await validator.IsValidAsync(userId, workshopId, tokenVersion, context.HttpContext.RequestAborted))
            context.Fail("Session is no longer valid.");
    }
}
