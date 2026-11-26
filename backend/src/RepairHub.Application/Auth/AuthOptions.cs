using System.ComponentModel.DataAnnotations;

namespace RepairHub.Application.Auth;

/// <summary>Configuration section "Auth".</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// A refresh token presented again within this window after it was rotated is treated as a benign race
    /// (two tabs refreshing at once) and simply rejected. Later reuse is treated as theft and revokes the whole family.
    /// </summary>
    [Range(0, 120)]
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}
