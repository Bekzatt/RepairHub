using RepairHub.Application.Abstractions;
using RepairHub.Domain.Users;

namespace RepairHub.Application.Common;

/// <summary>The authenticated caller with all fields present.</summary>
public sealed record Actor(Guid UserId, Guid WorkshopId, UserRole Role)
{
    public bool IsManager => WorkshopAccess.IsManager(Role);
    public bool IsFrontOffice => WorkshopAccess.IsFrontOffice(Role);
}

/// <summary>Role groups used by the customer, device and repair order rules.</summary>
public static class WorkshopAccess
{
    /// <summary>Owner and Admin.</summary>
    public static bool IsManager(UserRole? role) => role is UserRole.Owner or UserRole.Admin;

    /// <summary>Owner, Admin and Receptionist: customers, devices, intake, prices, assignment.</summary>
    public static bool IsFrontOffice(UserRole? role) => role is UserRole.Owner or UserRole.Admin or UserRole.Receptionist;

    /// <summary>Roles that can be assigned to (or take) a repair order. Never Receptionist.</summary>
    public static bool IsAssignableRole(UserRole role) => role is UserRole.Technician or UserRole.Admin or UserRole.Owner;

    public static readonly UserRole[] AssignableRoles = [UserRole.Technician, UserRole.Admin, UserRole.Owner];

    /// <summary>
    /// The caller, or 403 when there is no complete authenticated context. The role comes from the access token,
    /// which is rejected as soon as the user's role changes or the user is deactivated (token version check).
    /// </summary>
    public static Actor RequireActor(this ICurrentUser currentUser) =>
        currentUser is { UserId: { } userId, WorkshopId: { } workshopId, Role: { } role }
            ? new Actor(userId, workshopId, role)
            : throw new ForbiddenException();

    public static Actor RequireFrontOffice(this ICurrentUser currentUser)
    {
        var actor = currentUser.RequireActor();
        return actor.IsFrontOffice ? actor : throw new ForbiddenException();
    }

    public static Actor RequireManager(this ICurrentUser currentUser)
    {
        var actor = currentUser.RequireActor();
        return actor.IsManager ? actor : throw new ForbiddenException();
    }
}
