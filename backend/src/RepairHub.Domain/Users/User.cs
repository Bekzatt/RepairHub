using RepairHub.Domain.Common;

namespace RepairHub.Domain.Users;

/// <summary>A staff member of a workshop.</summary>
public sealed class User : ITenantOwned
{
    private User() { } // EF Core

    public Guid Id { get; private set; }
    public Guid WorkshopId { get; private set; }
    public string FullName { get; private set; } = null!;

    /// <summary>Email as entered by the user (for display).</summary>
    public string Email { get; private set; } = null!;

    /// <summary>Trimmed, lower-case email. Globally unique; used for login.</summary>
    public string NormalizedEmail { get; private set; } = null!;

    public string? Phone { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Incremented whenever existing access tokens must stop working
    /// (deactivation, role change, password change, token theft detection).
    /// </summary>
    public int TokenVersion { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static User Create(
        Guid workshopId, string fullName, string email, string? phone, UserRole role, string passwordHash, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            WorkshopId = workshopId,
            FullName = fullName.Trim(),
            Email = email.Trim(),
            NormalizedEmail = NormalizeEmail(email),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Role = role,
            PasswordHash = passwordHash,
            IsActive = true,
            TokenVersion = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

    public void UpdateProfile(string fullName, string? phone, DateTimeOffset now)
    {
        FullName = fullName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        UpdatedAt = now;
    }

    public void ChangeRole(UserRole role, DateTimeOffset now)
    {
        if (Role == role) return;
        Role = role;
        InvalidateSessions(now);
    }

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        InvalidateSessions(now);
    }

    /// <summary>Re-hash with stronger parameters without logging the user out.</summary>
    public void UpgradePasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        InvalidateSessions(now);
    }

    public void Activate(DateTimeOffset now)
    {
        if (IsActive) return;
        IsActive = true;
        UpdatedAt = now;
    }

    /// <summary>Makes every access token issued so far invalid on its next use.</summary>
    public void InvalidateSessions(DateTimeOffset now)
    {
        TokenVersion++;
        UpdatedAt = now;
    }
}
