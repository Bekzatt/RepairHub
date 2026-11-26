using System.ComponentModel.DataAnnotations;
using RepairHub.Application.Common;
using RepairHub.Domain.Devices;

namespace RepairHub.Application.Devices;

public sealed record DeviceDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    DeviceType Type,
    string Brand,
    string Model,
    string? SerialNumber,
    string? Imei,
    string? Description,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastReceivedAt);

public sealed record DeviceDetailsDto(DeviceDto Device, int OrderCount, int OpenOrderCount);

/// <summary>Fields shared by create/update and by "new device" during order creation.</summary>
public class DeviceFields
{
    [Required, EnumDataType(typeof(DeviceType))]
    public DeviceType? Type { get; init; }

    [Required, MaxLength(100)]
    public string Brand { get; init; } = string.Empty;

    [Required, MaxLength(150)]
    public string Model { get; init; } = string.Empty;

    [MaxLength(100)]
    public string? SerialNumber { get; init; }

    [MaxLength(32), Imei]
    public string? Imei { get; init; }

    [MaxLength(1000)]
    public string? Description { get; init; }
}

public sealed class CreateDeviceRequest : DeviceFields
{
    [Required]
    public Guid? CustomerId { get; init; }
}

public sealed class UpdateDeviceRequest : DeviceFields;

public sealed class DeviceListQuery
{
    /// <summary>Brand, model, serial number or IMEI fragment.</summary>
    [MaxLength(100)]
    public string? Search { get; init; }

    public Guid? CustomerId { get; init; }

    [EnumDataType(typeof(DeviceType))]
    public DeviceType? Type { get; init; }

    public bool IncludeArchived { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
