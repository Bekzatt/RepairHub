using System.ComponentModel.DataAnnotations;
using RepairHub.Domain.Users;

namespace RepairHub.Application.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(PasswordPolicy.MaxLength)]
    public string Password { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, MaxLength(PasswordPolicy.MaxLength)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, MaxLength(PasswordPolicy.MaxLength)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record WorkshopSummaryDto(Guid Id, string Name, string Currency, string TimeZone);

public sealed record CurrentUserDto(
    Guid Id, string FullName, string Email, string? Phone, UserRole Role, WorkshopSummaryDto Workshop);

/// <summary>Body returned by login / refresh / change-password. The refresh token travels only in the cookie.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, CurrentUserDto User);

/// <summary>Result of a successful sign-in: the response body plus the refresh token for the cookie.</summary>
public sealed record AuthSession(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);
