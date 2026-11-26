namespace RepairHub.Domain.Common;

internal static class Text
{
    /// <summary>Trims; blank becomes null.</summary>
    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Required(string value) => value.Trim();
}
