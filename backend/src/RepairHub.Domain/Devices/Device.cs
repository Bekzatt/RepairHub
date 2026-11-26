using RepairHub.Domain.Common;

namespace RepairHub.Domain.Devices;

/// <summary>
/// A customer's device. Reused when the same device comes back for another repair.
/// Serial number and IMEI are NOT unique: they can be missing, mistyped or duplicated in the real world.
/// </summary>
public sealed class Device : ITenantOwned
{
    private Device() { } // EF Core

    public Guid Id { get; private set; }
    public Guid WorkshopId { get; private set; }
    public Guid CustomerId { get; private set; }
    public DeviceType Type { get; private set; }
    public string Brand { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public string? SerialNumber { get; private set; }

    /// <summary>Digits only.</summary>
    public string? Imei { get; private set; }

    public string? Description { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Device Create(
        Guid workshopId, Guid customerId, DeviceType type, string brand, string model, string? serialNumber,
        string? imei, string? description, DateTimeOffset now)
    {
        var device = new Device
        {
            Id = Guid.CreateVersion7(now), WorkshopId = workshopId, CustomerId = customerId, CreatedAt = now,
        };
        device.Update(type, brand, model, serialNumber, imei, description, now);
        return device;
    }

    public void Update(
        DeviceType type, string brand, string model, string? serialNumber, string? imei, string? description,
        DateTimeOffset now)
    {
        Type = type;
        Brand = Text.Required(brand);
        Model = Text.Required(model);
        SerialNumber = Text.Optional(serialNumber);
        Imei = NormalizeImei(imei);
        Description = Text.Optional(description);
        UpdatedAt = now;
    }

    /// <summary>Removes spaces and hyphens; blank becomes null.</summary>
    public static string? NormalizeImei(string? imei)
    {
        var value = Text.Optional(imei);
        return value is null ? null : new string(value.Where(c => c is not (' ' or '-')).ToArray());
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
