namespace RepairHub.Application.Common;

/// <summary>
/// Expected failures of application operations. The API maps each type to an HTTP status code
/// and exposes <see cref="Code"/> so clients can show localized messages.
/// Messages are safe to show to clients and must not contain internal details.
/// </summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>404 — the resource does not exist or belongs to another workshop (indistinguishable on purpose).</summary>
public sealed class NotFoundException() : AppException("not_found", "The requested resource was not found.");

/// <summary>403 — the caller is authenticated but not allowed to perform the operation.</summary>
public sealed class ForbiddenException(string code = "forbidden", string message = "You do not have permission to perform this action.")
    : AppException(code, message);

/// <summary>409 — the operation conflicts with the current state (duplicate, last Owner, ...).</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message);

/// <summary>401 — credentials or refresh token are invalid. Deliberately generic.</summary>
public sealed class AuthenticationFailedException() : AppException("invalid_credentials", "Invalid credentials.");

/// <summary>400 — input is invalid. Keys are camelCase request field names.</summary>
public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("validation_failed", "One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public ValidationFailedException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] }) { }
}

/// <summary>Raised by the persistence layer when a unique constraint is violated.</summary>
public sealed class DuplicateKeyException(string? constraintName, Exception inner)
    : Exception($"Unique constraint '{constraintName}' violated.", inner)
{
    public string? ConstraintName { get; } = constraintName;
}

/// <summary>Stable error codes shared with the frontend.</summary>
public static class ErrorCodes
{
    public const string EmailTaken = "email_taken";
    public const string LastOwner = "last_owner";
    public const string SelfModification = "self_modification";
    public const string RoleNotAllowed = "role_not_allowed";
    public const string WorkshopsExist = "workshops_exist";
    public const string WorkshopNameTaken = "workshop_name_taken";
}
