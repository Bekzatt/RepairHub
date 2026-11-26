using RepairHub.Domain.Users;

namespace RepairHub.Application.Users;

/// <summary>Who may manage whom. The single source of truth for role hierarchy rules.</summary>
public static class UserPermissions
{
    public static bool CanViewUsers(UserRole? actor) => actor is UserRole.Owner or UserRole.Admin;

    /// <summary>
    /// Whether <paramref name="actor"/> may create, edit, (de)activate or reset the password of a user
    /// who has (or would get) <paramref name="role"/>.
    /// Owner: any role. Admin: Technician and Receptionist only. Others: nobody.
    /// </summary>
    public static bool CanManageRole(UserRole actor, UserRole role) => actor switch
    {
        UserRole.Owner => true,
        UserRole.Admin => role is UserRole.Technician or UserRole.Receptionist,
        _ => false,
    };
}
