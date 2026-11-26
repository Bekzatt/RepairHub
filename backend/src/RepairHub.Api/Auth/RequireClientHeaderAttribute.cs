using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RepairHub.Api.Auth;

/// <summary>
/// CSRF defence for cookie-authenticated endpoints (refresh, logout), in addition to SameSite=Strict:
/// a custom request header cannot be set by a cross-site form, and cross-origin fetch with it needs a CORS
/// preflight that this API does not allow.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireClientHeaderAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "RepairHub";

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Request.Headers[HeaderName] != HeaderValue)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing client header.",
                Extensions = { ["code"] = "client_header_required" },
            })
            { StatusCode = StatusCodes.Status400BadRequest };
            return Task.CompletedTask;
        }

        return next();
    }
}
