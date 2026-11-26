using System.ComponentModel.DataAnnotations;
using RepairHub.Domain.Common;

namespace RepairHub.Application.Common;

/// <summary>A phone number in any country format: digits, spaces and + ( ) -, with 5–20 digits. Null is valid.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PhoneNumberAttribute() : ValidationAttribute("Invalid phone number.")
{
    public override bool IsValid(object? value) =>
        value is null || (value is string s && (string.IsNullOrWhiteSpace(s) || PhoneNumber.IsValid(s)));
}
