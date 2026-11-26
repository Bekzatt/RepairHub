using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Domain.Users;

namespace RepairHub.Application.Workshops;

public sealed record WorkshopDto(
    Guid Id, string Name, string? Address, string? Phone, string Currency, string TimeZone, DateTimeOffset CreatedAt);

public sealed class UpdateWorkshopRequest
{
    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; init; }

    [MaxLength(32)]
    public string? Phone { get; init; }

    [Required, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO 4217 code.")]
    public string Currency { get; init; } = string.Empty;

    [Required, IanaTimeZone]
    public string TimeZone { get; init; } = string.Empty;
}

/// <summary>The current user's workshop. Tenant scoping comes from the query filter.</summary>
public sealed class WorkshopService(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<WorkshopDto> GetCurrentAsync(CancellationToken ct) =>
        await db.Workshops
            .Select(w => new WorkshopDto(w.Id, w.Name, w.Address, w.Phone, w.Currency, w.TimeZone, w.CreatedAt))
            .SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException();

    public async Task<WorkshopDto> UpdateCurrentAsync(UpdateWorkshopRequest request, CancellationToken ct)
    {
        // The endpoint policy already requires Owner; checked again here so the rule holds for any caller.
        if (currentUser.Role != UserRole.Owner)
            throw new ForbiddenException();

        var workshop = await db.Workshops.SingleOrDefaultAsync(ct) ?? throw new NotFoundException();
        workshop.UpdateDetails(request.Name, request.Address, request.Phone, request.Currency, request.TimeZone);
        await db.SaveChangesAsync(ct);

        return new WorkshopDto(
            workshop.Id, workshop.Name, workshop.Address, workshop.Phone, workshop.Currency, workshop.TimeZone,
            workshop.CreatedAt);
    }
}
