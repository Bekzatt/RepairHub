using Microsoft.EntityFrameworkCore;

namespace RepairHub.Application.Abstractions;

/// <summary>
/// The ONLY sanctioned way to read tenant-owned data across workshops.
/// <para>
/// Allowed callers are listed in <c>TenantFilterBypassTests</c>; adding a new caller requires
/// updating that list, which makes every bypass visible in code review.
/// Callers must always constrain the query themselves (by token hash, email or explicit ids).
/// </para>
/// </summary>
public static class TenantFilterBypass
{
    public const string FilterName = "Tenant";

    public static IQueryable<T> AcrossAllWorkshops<T>(this IQueryable<T> query) where T : class =>
        query.IgnoreQueryFilters([FilterName]);
}
