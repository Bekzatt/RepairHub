using RepairHub.Application.Common;

namespace RepairHub.Application.Auth;

/// <summary>
/// Password rules (NIST SP 800-63B style): length matters, composition rules do not.
/// 10–128 characters, not blank, not the same as the email.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static IReadOnlyList<string> Check(string? password, string? email)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        if (password.Length < MinLength) errors.Add($"Password must be at least {MinLength} characters long.");
        if (password.Length > MaxLength) errors.Add($"Password must be at most {MaxLength} characters long.");
        if (email is not null && string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add("Password must not be the same as the email.");

        return errors;
    }

    public static void EnsureValid(string? password, string? email, string field)
    {
        var errors = Check(password, email);
        if (errors.Count > 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { [field] = [.. errors] });
    }
}
