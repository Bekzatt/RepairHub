using System.ComponentModel.DataAnnotations;

namespace RepairHub.Application.Common;

/// <summary>
/// Runs the DataAnnotations rules of a request DTO. Controllers get this automatically via [ApiController];
/// non-HTTP entry points (the bootstrap command) call it explicitly so the same rules apply everywhere.
/// </summary>
public static class RequestValidator
{
    public static void ValidateOrThrow(object request)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true))
            return;

        var errors = results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [string.Empty])
                .Select(m => (Member: ToCamelCase(m), r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value.").ToArray());

        throw new ValidationFailedException(errors);
    }

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
