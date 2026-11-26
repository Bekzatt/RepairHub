namespace RepairHub.Domain.Users;

/// <summary>
/// A refresh token record. Only the SHA-256 hash of the token is stored.
/// Tokens issued from one login share a <see cref="FamilyId"/>; each refresh
/// revokes the presented token and issues a replacement in the same family.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { } // EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Issue(
        Guid userId, Guid familyId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.CreateVersion7(now),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
