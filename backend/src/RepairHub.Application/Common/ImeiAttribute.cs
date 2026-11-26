using System.ComponentModel.DataAnnotations;
using RepairHub.Domain.Devices;

namespace RepairHub.Application.Common;

/// <summary>Optional IMEI: 14–16 digits once spaces and hyphens are removed.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ImeiAttribute() : ValidationAttribute("IMEI must contain 14–16 digits.")
{
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not string s) return false;
        var normalized = Device.NormalizeImei(s);
        return normalized is null || (normalized.Length is >= 14 and <= 16 && normalized.All(char.IsAsciiDigit));
    }
}
