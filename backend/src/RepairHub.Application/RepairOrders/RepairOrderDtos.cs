using System.ComponentModel.DataAnnotations;
using RepairHub.Application.Common;
using RepairHub.Application.Customers;
using RepairHub.Application.Devices;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;

namespace RepairHub.Application.RepairOrders;

public sealed record UserRefDto(Guid Id, string FullName);

public sealed record AssigneeDto(Guid Id, string FullName, UserRole Role);

public sealed record RepairOrderListItemDto(
    Guid Id,
    string Number,
    RepairOrderStatus Status,
    RepairOutcome? Outcome,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    Guid DeviceId,
    DeviceType DeviceType,
    string DeviceBrand,
    string DeviceModel,
    string Problem,
    UserRefDto? AssignedTechnician,
    DateTimeOffset ReceivedAt,
    DateOnly? ExpectedCompletionDate,
    bool IsOverdue,
    decimal? EstimatedPrice,
    decimal? FinalPrice,
    string Currency,
    DateTimeOffset UpdatedAt);

/// <summary>A status change the current user may perform now. Computed by the server from the workflow table.</summary>
public sealed record AvailableTransitionDto(
    RepairOrderStatus To,
    bool RequiresOutcome,
    IReadOnlyList<RepairOutcome> Outcomes,
    bool RequiresReason);

/// <summary>What the current user may do with the order. The server enforces the same rules on every request.</summary>
public sealed record RepairOrderPermissionsDto(
    bool EditIntake,
    bool EditDiagnostics,
    bool EditTerms,
    bool CorrectSnapshot,
    bool Assign,
    bool Take,
    bool AddNote);

public sealed record RepairOrderDetailsDto(
    Guid Id,
    string Number,
    RepairOrderStatus Status,
    RepairOutcome? Outcome,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    Guid DeviceId,
    DeviceType DeviceType,
    string DeviceBrand,
    string DeviceModel,
    string? DeviceSerialNumber,
    string? DeviceImei,
    string Problem,
    string? Condition,
    string? Accessories,
    string? Diagnostics,
    decimal? EstimatedPrice,
    decimal? FinalPrice,
    string Currency,
    UserRefDto? AssignedTechnician,
    UserRefDto CreatedBy,
    UserRefDto UpdatedBy,
    DateTimeOffset ReceivedAt,
    DateOnly? ExpectedCompletionDate,
    bool IsOverdue,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    IReadOnlyList<AvailableTransitionDto> AllowedTransitions,
    RepairOrderPermissionsDto Can);

public sealed record RepairOrderEventDto(
    Guid Id,
    RepairOrderEventType Type,
    string? OldValue,
    string? NewValue,
    string? Comment,
    UserRefDto Actor,
    DateTimeOffset OccurredAt);

public sealed record StatusCountDto(RepairOrderStatus Status, int Count);

public sealed record RepairOrderSummaryDto(IReadOnlyList<StatusCountDto> ByStatus, int Overdue, int Unassigned);

public sealed class NewDeviceRequest : DeviceFields;

/// <summary>
/// Creates the order in one transaction, optionally with a new customer and/or a new device.
/// Exactly one of <see cref="CustomerId"/>/<see cref="NewCustomer"/> and one of <see cref="DeviceId"/>/<see cref="NewDevice"/>.
/// </summary>
public sealed class CreateRepairOrderRequest : IValidatableObject
{
    public Guid? CustomerId { get; init; }
    public CreateCustomerRequest? NewCustomer { get; init; }
    public Guid? DeviceId { get; init; }
    public NewDeviceRequest? NewDevice { get; init; }

    [Required, MaxLength(2000)]
    public string Problem { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Condition { get; init; }

    [MaxLength(1000)]
    public string? Accessories { get; init; }

    public Guid? AssignedTechnicianId { get; init; }

    public DateOnly? ExpectedCompletionDate { get; init; }

    [Money]
    public decimal? EstimatedPrice { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((CustomerId is null) == (NewCustomer is null))
            yield return new ValidationResult("Specify either customerId or newCustomer.", [nameof(CustomerId)]);
        if ((DeviceId is null) == (NewDevice is null))
            yield return new ValidationResult("Specify either deviceId or newDevice.", [nameof(DeviceId)]);
        if (NewCustomer is not null && DeviceId is not null)
            yield return new ValidationResult("A new customer cannot own an existing device.", [nameof(DeviceId)]);
    }
}

public abstract class VersionedRequest
{
    /// <summary>The version the client last read; a different current version means 409 concurrency_conflict.</summary>
    [Required]
    public uint? Version { get; init; }
}

public sealed class UpdateIntakeRequest : VersionedRequest
{
    [Required, MaxLength(2000)]
    public string Problem { get; init; } = string.Empty;

    [MaxLength(2000)]
    public string? Condition { get; init; }

    [MaxLength(1000)]
    public string? Accessories { get; init; }
}

public sealed class UpdateDiagnosticsRequest : VersionedRequest
{
    [MaxLength(4000)]
    public string? Diagnostics { get; init; }
}

public sealed class UpdateTermsRequest : VersionedRequest
{
    [Money]
    public decimal? EstimatedPrice { get; init; }

    [Money]
    public decimal? FinalPrice { get; init; }

    public DateOnly? ExpectedCompletionDate { get; init; }
}

/// <summary>Corrects the customer/device details stored on the order (not the customer or device records).</summary>
public sealed class CorrectSnapshotRequest : VersionedRequest
{
    [Required, MaxLength(200)]
    public string CustomerName { get; init; } = string.Empty;

    [Required, MaxLength(32), PhoneNumber]
    public string CustomerPhone { get; init; } = string.Empty;

    [Required, EnumDataType(typeof(DeviceType))]
    public DeviceType? DeviceType { get; init; }

    [Required, MaxLength(100)]
    public string DeviceBrand { get; init; } = string.Empty;

    [Required, MaxLength(150)]
    public string DeviceModel { get; init; } = string.Empty;

    [MaxLength(100)]
    public string? DeviceSerialNumber { get; init; }

    [MaxLength(32), Imei]
    public string? DeviceImei { get; init; }
}

public sealed class AssignTechnicianRequest : VersionedRequest
{
    /// <summary>Null unassigns.</summary>
    public Guid? TechnicianId { get; init; }
}

public sealed class ChangeStatusRequest : VersionedRequest
{
    [Required, EnumDataType(typeof(RepairOrderStatus))]
    public RepairOrderStatus? Status { get; init; }

    [EnumDataType(typeof(RepairOutcome))]
    public RepairOutcome? Outcome { get; init; }

    /// <summary>Optional comment; required (the reason) when cancelling.</summary>
    [MaxLength(500)]
    public string? Comment { get; init; }
}

public sealed class AddNoteRequest
{
    [Required, MaxLength(2000)]
    public string Text { get; init; } = string.Empty;
}

public enum RepairOrderSort
{
    ReceivedAt,
    ExpectedCompletionDate,
    Number,
    UpdatedAt,
}

public sealed class RepairOrderListQuery
{
    /// <summary>Order number (or its digits), customer name or phone, device brand/model/serial/IMEI.</summary>
    [MaxLength(100)]
    public string? Search { get; init; }

    /// <summary>Any of these statuses; empty = all.</summary>
    public RepairOrderStatus[]? Status { get; init; }

    /// <summary>"me", "unassigned" or a user id.</summary>
    [MaxLength(40)]
    public string? Technician { get; init; }

    public Guid? CustomerId { get; init; }
    public Guid? DeviceId { get; init; }

    /// <summary>Expected date before today (workshop time zone) and the repair not finished.</summary>
    public bool Overdue { get; init; }

    [EnumDataType(typeof(RepairOrderSort))]
    public RepairOrderSort Sort { get; init; } = RepairOrderSort.ReceivedAt;

    public bool Desc { get; init; } = true;

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed class RepairOrderSummaryQuery
{
    /// <summary>"me", "unassigned" or a user id.</summary>
    [MaxLength(40)]
    public string? Technician { get; init; }
}
