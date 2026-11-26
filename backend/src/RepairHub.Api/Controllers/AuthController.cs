using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RepairHub.Api.Auth;
using RepairHub.Application.Auth;
using RepairHub.Application.Common;

namespace RepairHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthService auth, RefreshTokenCookie refreshCookie) : ControllerBase
{
    /// <summary>Signs in with email and password. Sets the refresh-token cookie.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationSetup.LoginRateLimit)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var session = await auth.LoginAsync(request, ct);
        return SessionResult(session);
    }

    /// <summary>Exchanges the refresh-token cookie for a new access token and a rotated refresh token.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [RequireClientHeader]
    [EnableRateLimiting(AuthenticationSetup.RefreshRateLimit)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        try
        {
            var session = await auth.RefreshAsync(refreshCookie.Read(Request), ct);
            return SessionResult(session);
        }
        catch (AuthenticationFailedException ex)
        {
            // Answer here instead of rethrowing: the exception handler clears response headers,
            // which would drop the cookie deletion.
            refreshCookie.Delete(Response);
            return new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = ex.Message,
                Extensions = { ["code"] = ex.Code },
            })
            { StatusCode = StatusCodes.Status401Unauthorized };
        }
    }

    /// <summary>Revokes the current session and clears the cookie. Always succeeds.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [RequireClientHeader]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await auth.LogoutAsync(refreshCookie.Read(Request), ct);
        refreshCookie.Delete(Response);
        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct) => Ok(await auth.GetMeAsync(ct));

    /// <summary>Changes the caller's password and signs out all other sessions.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var session = await auth.ChangePasswordAsync(request, ct);
        return SessionResult(session);
    }

    private OkObjectResult SessionResult(AuthSession session)
    {
        refreshCookie.Write(Response, session.RefreshToken, session.RefreshTokenExpiresAt);
        Response.Headers.CacheControl = "no-store";
        return Ok(session.Response);
    }
}
