using System.ComponentModel.DataAnnotations;
using RepairHub.Application.Common;

namespace RepairHub.Application.Customers;

public sealed record CustomerDto(
    Guid Id,
    string FullName,
    string Phone,
    string? AdditionalPhone,
    string? Email,
    string? Notes,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CustomerDetailsDto(
    CustomerDto Customer,
    int DeviceCount,
    int OrderCount,
    int OpenOrderCount);

/// <summary>Returned in the "matches" field of a 409 customer_phone_exists response.</summary>
public sealed record CustomerMatchDto(Guid Id, string FullName, string Phone, string? AdditionalPhone, bool IsArchived);

/// <summary>Fields shared by create and update, and by "new customer" during order creation.</summary>
public class CustomerFields
{
    [Required, MaxLength(200)]
    public string FullName { get; init; } = string.Empty;

    [Required, MaxLength(32), PhoneNumber]
    public string Phone { get; init; } = string.Empty;

    [MaxLength(32), PhoneNumber]
    public string? AdditionalPhone { get; init; }

    [EmailAddress, MaxLength(254)]
    public string? Email { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    /// <summary>Set after the user confirmed that a customer with the same phone is a different person.</summary>
    public bool AllowDuplicatePhone { get; init; }
}

public sealed class CreateCustomerRequest : CustomerFields;

public sealed class UpdateCustomerRequest : CustomerFields;

public enum CustomerSort
{
    Name,
    CreatedAt,
}

public sealed class CustomerListQuery
{
    /// <summary>Name, phone or email fragment.</summary>
    [MaxLength(100)]
    public string? Search { get; init; }

    /// <summary>Exact phone match by match key (duplicate hints while typing).</summary>
    [MaxLength(32)]
    public string? Phone { get; init; }

    public bool IncludeArchived { get; init; }

    [EnumDataType(typeof(CustomerSort))]
    public CustomerSort Sort { get; init; } = CustomerSort.Name;

    public bool Desc { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
