using System.ComponentModel.DataAnnotations;

namespace RepairHub.Application.Common;

/// <summary>Optional money amount: 0 ≤ value ≤ 999 999 999 999.99 with at most two decimal places.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MoneyAttribute() : ValidationAttribute("Invalid amount.")
{
    public const decimal Max = 999_999_999_999.99m;

    public override bool IsValid(object? value) =>
        value is null || (value is decimal d && d is >= 0 and <= Max && decimal.Round(d, 2) == d);
}
