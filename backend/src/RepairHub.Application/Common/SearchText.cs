using RepairHub.Domain.Common;

namespace RepairHub.Application.Common;

/// <summary>Helpers for case-insensitive "contains" searches.</summary>
public static class SearchText
{
    /// <summary>
    /// Lower-case "%term%" with the LIKE wildcards in the user's input escaped with '\'.
    /// Use as <c>EF.Functions.Like(column.ToLower(), pattern, "\\")</c>, which stays provider-neutral.
    /// </summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Trim().ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>
    /// The phone match key to search for when the term looks like (part of) a phone number:
    /// at least 3 digits and only phone characters. Otherwise null.
    /// </summary>
    public static string? PhoneDigits(string term)
    {
        term = term.Trim();
        if (term.Any(c => !char.IsAsciiDigit(c) && c is not (' ' or '+' or '(' or ')' or '-'))) return null;
        var key = PhoneNumber.MatchKey(term);
        return key is { Length: >= 3 } ? key : null;
    }
}
