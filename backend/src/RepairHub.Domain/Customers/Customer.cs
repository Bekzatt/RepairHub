using RepairHub.Domain.Common;

namespace RepairHub.Domain.Customers;

/// <summary>A client of the workshop. Never deleted: archived instead, so repair history stays intact.</summary>
public sealed class Customer : ITenantOwned
{
    private Customer() { } // EF Core

    public Guid Id { get; private set; }
    public Guid WorkshopId { get; private set; }
    public string FullName { get; private set; } = null!;

    /// <summary>As entered.</summary>
    public string Phone { get; private set; } = null!;

    /// <summary>See <see cref="PhoneNumber.MatchKey"/>.</summary>
    public string PhoneKey { get; private set; } = null!;

    public string? AdditionalPhone { get; private set; }
    public string? AdditionalPhoneKey { get; private set; }
    public string? Email { get; private set; }
    public string? Notes { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Customer Create(
        Guid workshopId, string fullName, string phone, string? additionalPhone, string? email, string? notes,
        DateTimeOffset now)
    {
        var customer = new Customer { Id = Guid.CreateVersion7(now), WorkshopId = workshopId, CreatedAt = now };
        customer.Update(fullName, phone, additionalPhone, email, notes, now);
        return customer;
    }

    public void Update(string fullName, string phone, string? additionalPhone, string? email, string? notes, DateTimeOffset now)
    {
        FullName = Text.Required(fullName);
        Phone = Text.Required(phone);
        PhoneKey = PhoneNumber.MatchKey(phone) ?? throw new ArgumentException("Phone has no digits.", nameof(phone));
        AdditionalPhone = Text.Optional(additionalPhone);
        AdditionalPhoneKey = PhoneNumber.MatchKey(AdditionalPhone);
        Email = Text.Optional(email);
        Notes = Text.Optional(notes);
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        if (IsArchived) return;
        IsArchived = true;
        ArchivedAt = now;
        UpdatedAt = now;
    }

    public void Restore(DateTimeOffset now)
    {
        if (!IsArchived) return;
        IsArchived = false;
        ArchivedAt = null;
        UpdatedAt = now;
    }
}
