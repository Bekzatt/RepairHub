namespace RepairHub.Domain.Users;

/// <summary>Staff roles. Stored as text in the database; do not rename members without a migration.</summary>
public enum UserRole
{
    Owner = 1,
    Admin = 2,
    Technician = 3,
    Receptionist = 4,
}
