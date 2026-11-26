using Microsoft.EntityFrameworkCore;
using RepairHub.Application.Abstractions;
using RepairHub.Domain.Workshops;

namespace RepairHub.Application.Common;

/// <summary>Instants are stored in UTC; calendar dates ("today", the numbering year) use the workshop's time zone.</summary>
public static class WorkshopTime
{
    public static DateOnly Today(DateTimeOffset utcNow, string timeZoneId)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found)
            ? found
            : TimeZoneInfo.FindSystemTimeZoneById(Workshop.DefaultTimeZone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
    }

    public sealed record WorkshopSettings(string TimeZone, string Currency);

    /// <summary>The current workshop's time zone and currency (tenant-filtered).</summary>
    public static async Task<WorkshopSettings> LoadCurrentWorkshopAsync(this IAppDbContext db, CancellationToken ct) =>
        await db.Workshops.AsNoTracking()
            .Select(w => new WorkshopSettings(w.TimeZone, w.Currency))
            .SingleOrDefaultAsync(ct)
        ?? throw new ForbiddenException();
}
