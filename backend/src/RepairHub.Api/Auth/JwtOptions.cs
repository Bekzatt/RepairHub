using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RepairHub.Api.Auth;

/// <summary>Configuration section "Jwt". The signing key must come from the environment (Jwt__SigningKey).</summary>
public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";
    private const int MinKeyBytes = 32; // 256 bits for HMAC-SHA256

    [Required]
    public string Issuer { get; set; } = "repairhub";

    [Required]
    public string Audience { get; set; } = "repairhub";

    [Required]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 60)]
    public int AccessTokenMinutes { get; set; } = 15;

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < MinKeyBytes)
            yield return new ValidationResult(
                $"Jwt:SigningKey must be at least {MinKeyBytes} bytes. Set the Jwt__SigningKey environment variable.",
                [nameof(SigningKey)]);
    }
}
