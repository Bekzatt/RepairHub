using System.ComponentModel.DataAnnotations;
using RepairHub.Application.Auth;
using RepairHub.Domain.Users;

namespace RepairHub.Application.Users;

public sealed record UserDto(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    UserRole Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed class CreateUserRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;

    [MaxLength(32)]
    public string? Phone { get; init; }

    [Required, EnumDataType(typeof(UserRole))]
    public UserRole? Role { get; init; }

    [Required, MaxLength(PasswordPolicy.MaxLength)]
    public string Password { get; init; } = string.Empty;
}

public sealed class UpdateUserRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    [MaxLength(32)]
    public string? Phone { get; init; }

    [Required, EnumDataType(typeof(UserRole))]
    public UserRole? Role { get; init; }
}

public sealed class ResetPasswordRequest
{
    [Required, MaxLength(PasswordPolicy.MaxLength)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class UserListQuery
{
    [MaxLength(100)]
    public string? Search { get; init; }

    [EnumDataType(typeof(UserRole))]
    public UserRole? Role { get; init; }

    public bool? IsActive { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
