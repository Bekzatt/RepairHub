using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using RepairHub.Application.Abstractions;

namespace RepairHub.Infrastructure.Security;

/// <summary>
/// Wraps ASP.NET Core Identity's hasher (PBKDF2-HMAC-SHA512, random salt, versioned format)
/// without pulling in the rest of ASP.NET Core Identity.
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>
    /// OWASP (2023) recommendation for PBKDF2-HMAC-SHA512. Hashes created with fewer iterations
    /// verify as SuccessRehashNeeded and are upgraded transparently at the next login.
    /// </summary>
    public const int IterationCount = 210_000;

    private static readonly PasswordHasher<object> Inner =
        new(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));

    private static readonly object User = new(); // the Identity hasher ignores the user argument

    public string Hash(string password) => Inner.HashPassword(User, password);

    public PasswordCheck Verify(string passwordHash, string password) =>
        Inner.VerifyHashedPassword(User, passwordHash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
}
