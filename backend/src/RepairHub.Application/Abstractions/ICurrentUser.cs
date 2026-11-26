using RepairHub.Domain.Users;

namespace RepairHub.Application.Abstractions;

/// <summary>The authenticated caller. All members are null when there is no authenticated user.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? WorkshopId { get; }
    UserRole? Role { get; }
}
