using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;

namespace RepairHub.Application.RepairOrders;

/// <summary>
/// Read side of repair orders. Every role sees every order of its own workshop (tenant filter);
/// listing a customer's or a device's orders belongs to the front-office customer/device pages.
/// </summary>
public sealed partial class RepairOrderQueries(IAppDbContext db, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<RepairOrderDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        var actor = currentUser.RequireActor();
        var order = await db.RepairOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException();
        var today = await TodayAsync(ct);

        var userIds = new[] { order.AssignedTechnicianId, order.CreatedById, order.UpdatedById }.OfType<Guid>().Distinct().ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        UserRefDto Ref(Guid userId) => new(userId, names.GetValueOrDefault(userId, "—"));

        var transitions = RepairOrderWorkflow.AvailableTo(order, actor.Role, actor.UserId)
            .Select(t => new AvailableTransitionDto(t.To, t.RequiresOutcome, t.Outcomes, t.RequiresReason))
            .ToList();

        return new RepairOrderDetailsDto(
            order.Id, order.Number, order.Status, order.Outcome,
            order.CustomerId, order.CustomerName, order.CustomerPhone,
            order.DeviceId, order.DeviceType, order.DeviceBrand, order.DeviceModel, order.DeviceSerialNumber, order.DeviceImei,
            order.Problem, order.Condition, order.Accessories, order.Diagnostics,
            order.EstimatedPrice, order.FinalPrice, order.Currency,
            order.AssignedTechnicianId is { } technicianId ? Ref(technicianId) : null,
            Ref(order.CreatedById), Ref(order.UpdatedById),
            order.ReceivedAt, order.ExpectedCompletionDate,
            IsOverdue(order.ExpectedCompletionDate, order.Status, today),
            order.StartedAt, order.CompletedAt, order.IssuedAt, order.CancelledAt, order.CancellationReason,
            order.CreatedAt, order.UpdatedAt, order.Version,
            transitions,
            RepairOrderPermissions.For(actor, order));
    }

    public async Task<PagedResult<RepairOrderListItemDto>> ListAsync(RepairOrderListQuery query, CancellationToken ct)
    {
        var actor = currentUser.RequireActor();
        var today = await TodayAsync(ct);

        var orders = FilterByTechnician(db.RepairOrders.AsNoTracking(), query.Technician, actor);
        if (query.Status is { Length: > 0 } statuses) orders = orders.Where(o => statuses.Contains(o.Status));
        if (query.CustomerId is { } customerId) orders = orders.Where(o => o.CustomerId == customerId);
        if (query.DeviceId is { } deviceId) orders = orders.Where(o => o.DeviceId == deviceId);
        if (query.Overdue) orders = WhereOverdue(orders, today);
        if (!string.IsNullOrWhiteSpace(query.Search)) orders = Search(orders, query.Search);

        orders = (query.Sort, query.Desc) switch
        {
            (RepairOrderSort.Number, false) => orders.OrderBy(o => o.NumberYear).ThenBy(o => o.NumberSequence),
            (RepairOrderSort.Number, true) => orders.OrderByDescending(o => o.NumberYear).ThenByDescending(o => o.NumberSequence),
            (RepairOrderSort.ExpectedCompletionDate, false) => orders.OrderBy(o => o.ExpectedCompletionDate).ThenBy(o => o.Id),
            (RepairOrderSort.ExpectedCompletionDate, true) => orders.OrderByDescending(o => o.ExpectedCompletionDate).ThenByDescending(o => o.Id),
            (RepairOrderSort.UpdatedAt, false) => orders.OrderBy(o => o.UpdatedAt).ThenBy(o => o.Id),
            (RepairOrderSort.UpdatedAt, true) => orders.OrderByDescending(o => o.UpdatedAt).ThenByDescending(o => o.Id),
            (_, false) => orders.OrderBy(o => o.ReceivedAt).ThenBy(o => o.Id),
            (_, true) => orders.OrderByDescending(o => o.ReceivedAt).ThenByDescending(o => o.Id),
        };

        return await PageAsync(orders, query.Page, query.PageSize, today, ct);
    }

    public async Task<PagedResult<RepairOrderListItemDto>> ListForCustomerAsync(Guid customerId, PageQuery page, CancellationToken ct)
    {
        currentUser.RequireFrontOffice();
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct)) throw new NotFoundException();
        var orders = db.RepairOrders.AsNoTracking().Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.ReceivedAt).ThenByDescending(o => o.Id);
        return await PageAsync(orders, page.Page, page.PageSize, await TodayAsync(ct), ct);
    }

    public async Task<PagedResult<RepairOrderListItemDto>> ListForDeviceAsync(Guid deviceId, PageQuery page, CancellationToken ct)
    {
        currentUser.RequireFrontOffice();
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, ct)) throw new NotFoundException();
        var orders = db.RepairOrders.AsNoTracking().Where(o => o.DeviceId == deviceId)
            .OrderByDescending(o => o.ReceivedAt).ThenByDescending(o => o.Id);
        return await PageAsync(orders, page.Page, page.PageSize, await TodayAsync(ct), ct);
    }

    /// <summary>Counts per status (for the filter chips), overdue and unassigned open orders.</summary>
    public async Task<RepairOrderSummaryDto> SummaryAsync(RepairOrderSummaryQuery query, CancellationToken ct)
    {
        var actor = currentUser.RequireActor();
        var today = await TodayAsync(ct);
        var orders = FilterByTechnician(db.RepairOrders.AsNoTracking(), query.Technician, actor);

        var counts = await orders.GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var overdue = await WhereOverdue(orders, today).CountAsync(ct);
        var unassigned = await db.RepairOrders.CountAsync(
            o => o.AssignedTechnicianId == null && !RepairOrderWorkflow.ClosedStatuses.Contains(o.Status), ct);

        var byStatus = Enum.GetValues<RepairOrderStatus>()
            .Select(s => new StatusCountDto(s, counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0))
            .ToList();
        return new RepairOrderSummaryDto(byStatus, overdue, unassigned);
    }

    /// <summary>The order's history, oldest first. Technician ids in assignment events are resolved to names.</summary>
    public async Task<IReadOnlyList<RepairOrderEventDto>> HistoryAsync(Guid id, CancellationToken ct)
    {
        currentUser.RequireActor();
        if (!await db.RepairOrders.AnyAsync(o => o.Id == id, ct)) throw new NotFoundException();

        // Events written by one operation share the same timestamp (and UUIDv7 ids are not ordered within a
        // millisecond), so ties are broken by the event type's declaration order: Created before
        // TechnicianAssigned, StatusChanged before OutcomeRecorded.
        var events = (await db.RepairOrderEvents.AsNoTracking()
                .Where(e => e.RepairOrderId == id)
                .ToListAsync(ct))
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Type).ThenBy(e => e.Id)
            .ToList();

        var userIds = events.Select(e => e.ActorUserId)
            .Concat(events.Where(e => e.Type == RepairOrderEventType.TechnicianAssigned)
                .SelectMany(e => new[] { e.OldValue, e.NewValue })
                .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null)
                .OfType<Guid>())
            .Distinct()
            .ToList();
        var names = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        string? Name(string? value) =>
            Guid.TryParse(value, out var userId) ? names.GetValueOrDefault(userId, value) : value;

        return events.Select(e =>
        {
            var assignment = e.Type == RepairOrderEventType.TechnicianAssigned;
            return new RepairOrderEventDto(
                e.Id, e.Type,
                assignment ? Name(e.OldValue) : e.OldValue,
                assignment ? Name(e.NewValue) : e.NewValue,
                e.Comment,
                new UserRefDto(e.ActorUserId, names.GetValueOrDefault(e.ActorUserId, "—")),
                e.OccurredAt);
        }).ToList();
    }

    /// <summary>Active users who can be assigned to orders (Technician, Admin, Owner). Available to every role.</summary>
    public async Task<IReadOnlyList<AssigneeDto>> AssigneesAsync(CancellationToken ct)
    {
        currentUser.RequireActor();
        return await db.Users.AsNoTracking()
            .Where(u => u.IsActive && WorkshopAccess.AssignableRoles.Contains(u.Role))
            .OrderBy(u => u.FullName)
            .Select(u => new AssigneeDto(u.Id, u.FullName, u.Role))
            .ToListAsync(ct);
    }

    private async Task<PagedResult<RepairOrderListItemDto>> PageAsync(
        IQueryable<RepairOrder> orders, int page, int pageSize, DateOnly today, CancellationToken ct)
    {
        var total = await orders.CountAsync(ct);
        var items = await orders
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new RepairOrderListItemDto(
                o.Id, o.Number, o.Status, o.Outcome,
                o.CustomerId, o.CustomerName, o.CustomerPhone,
                o.DeviceId, o.DeviceType, o.DeviceBrand, o.DeviceModel,
                o.Problem,
                db.Users.Where(u => u.Id == o.AssignedTechnicianId).Select(u => new UserRefDto(u.Id, u.FullName)).FirstOrDefault(),
                o.ReceivedAt, o.ExpectedCompletionDate,
                o.ExpectedCompletionDate < today && RepairOrderWorkflow.InProgressStatuses.Contains(o.Status),
                o.EstimatedPrice, o.FinalPrice, o.Currency, o.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<RepairOrderListItemDto>(items, page, pageSize, total);
    }

    private static IQueryable<RepairOrder> FilterByTechnician(IQueryable<RepairOrder> orders, string? technician, Actor actor)
    {
        if (string.IsNullOrWhiteSpace(technician)) return orders;
        if (technician.Equals("me", StringComparison.OrdinalIgnoreCase))
            return orders.Where(o => o.AssignedTechnicianId == actor.UserId);
        if (technician.Equals("unassigned", StringComparison.OrdinalIgnoreCase))
            return orders.Where(o => o.AssignedTechnicianId == null);
        if (Guid.TryParse(technician, out var technicianId))
            return orders.Where(o => o.AssignedTechnicianId == technicianId);
        throw new ValidationFailedException("technician", "Use \"me\", \"unassigned\" or a user id.");
    }

    private static IQueryable<RepairOrder> WhereOverdue(IQueryable<RepairOrder> orders, DateOnly today) =>
        orders.Where(o => o.ExpectedCompletionDate < today && RepairOrderWorkflow.InProgressStatuses.Contains(o.Status));

    private static bool IsOverdue(DateOnly? expected, RepairOrderStatus status, DateOnly today) =>
        expected < today && RepairOrderWorkflow.InProgressStatuses.Contains(status);

    private IQueryable<RepairOrder> Search(IQueryable<RepairOrder> orders, string term)
    {
        var pattern = SearchText.ContainsPattern(term);
        var phoneDigits = SearchText.PhoneDigits(term);
        var imei = Device.NormalizeImei(term);
        int? sequence = OrderNumberPattern().Match(term.Trim()) is { Success: true } m && int.TryParse(m.Groups["seq"].Value, out var s)
            ? s
            : null;

        return orders.Where(o =>
            EF.Functions.Like(o.Number.ToLower(), pattern, "\\")
            || (sequence != null && o.NumberSequence == sequence)
            || EF.Functions.Like(o.CustomerName.ToLower(), pattern, "\\")
            || EF.Functions.Like(o.CustomerPhone.ToLower(), pattern, "\\")
            || EF.Functions.Like((o.DeviceBrand + " " + o.DeviceModel).ToLower(), pattern, "\\")
            || EF.Functions.Like(o.DeviceSerialNumber!.ToLower(), pattern, "\\")
            || (imei != null && o.DeviceImei!.Contains(imei))
            // The customer's CURRENT name/phone too, so orders are found after a phone number change.
            || db.Customers.Any(c => c.Id == o.CustomerId
                && (EF.Functions.Like(c.FullName.ToLower(), pattern, "\\")
                    || (phoneDigits != null && (c.PhoneKey.Contains(phoneDigits) || c.AdditionalPhoneKey!.Contains(phoneDigits))))));
    }

    /// <summary>"152", "000152", "2026-000152", "RH-2026-000152".</summary>
    [GeneratedRegex(@"^(?:RH-?)?(?:\d{4}-)?0*(?<seq>\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex OrderNumberPattern();

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var workshop = await db.LoadCurrentWorkshopAsync(ct);
        return WorkshopTime.Today(time.GetUtcNow(), workshop.TimeZone);
    }
}
