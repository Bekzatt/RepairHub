namespace RepairHub.Domain.Common;

/// <summary>
/// Phone numbers are stored exactly as entered (any country format) plus a <i>match key</i> used to detect
/// duplicates: the digits only, keeping the last 10. "+7 701 123 45 67", "8 (701) 123-45-67" and "7011234567"
/// share the key "7011234567". The key decides what counts as "the same number"; it is not a format rule.
/// </summary>
public static class PhoneNumber
{
    public const int MinDigits = 5;
    public const int MaxDigits = 20;
    public const int KeyLength = 10;

    /// <summary>Digits, spaces and + ( ) - only, with 5–20 digits.</summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '+' or '(' or ')' or '-'))) return false;
        var digits = value.Count(char.IsAsciiDigit);
        return digits is >= MinDigits and <= MaxDigits;
    }

    public static string Digits(string value) => new(value.Where(char.IsAsciiDigit).ToArray());

    /// <summary>The duplicate-detection key, or null when the value has no digits.</summary>
    public static string? MatchKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = Digits(value);
        if (digits.Length == 0) return null;
        return digits.Length > KeyLength ? digits[^KeyLength..] : digits;
    }
}
