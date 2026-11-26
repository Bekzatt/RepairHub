using System.ComponentModel.DataAnnotations;

namespace RepairHub.Application.Common;

/// <summary>Accepts only IANA time zone ids known to the runtime (e.g. "Asia/Almaty").</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class IanaTimeZoneAttribute() : ValidationAttribute("Unknown time zone.")
{
    public override bool IsValid(object? value) =>
        value is null || (value is string id
            && id.Contains('/', StringComparison.Ordinal)
            && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _));
}
