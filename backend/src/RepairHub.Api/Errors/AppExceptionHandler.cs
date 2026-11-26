using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RepairHub.Application.Common;
using RepairHub.Domain.Common;

namespace RepairHub.Api.Errors;

/// <summary>
/// Maps expected application errors to RFC 7807 responses with a stable "code" extension.
/// Anything else falls through to the default handler (generic 500, details only in logs).
/// </summary>
internal sealed class AppExceptionHandler(IProblemDetailsService problemDetails, ILogger<AppExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, title) = exception switch
        {
            ValidationFailedException e => (StatusCodes.Status400BadRequest, e.Code, e.Message),
            InvalidRequestException e => (StatusCodes.Status400BadRequest, e.Code, e.Message),
            AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, e.Code, e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Code, e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            ConcurrencyConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            // Business rules depend on the current state of the resource (e.g. its status): 409.
            DomainRuleException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            DuplicateKeyException => (StatusCodes.Status409Conflict, "duplicate", "The resource already exists."),
            _ => (0, null, null),
        };
        if (status == 0) return false;

        logger.LogDebug("Request failed with {StatusCode} ({ErrorCode})", status, code);

        ProblemDetails body = exception is ValidationFailedException validation
            ? new HttpValidationProblemDetails(validation.Errors.ToDictionary(kv => kv.Key, kv => kv.Value))
            : new ProblemDetails();
        body.Status = status;
        body.Title = title;
        body.Extensions["code"] = code;
        if (exception is AppException { Details: { } details })
            foreach (var (key, value) in details) body.Extensions[key] = value;

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = body,
            Exception = exception,
        });
    }
}
