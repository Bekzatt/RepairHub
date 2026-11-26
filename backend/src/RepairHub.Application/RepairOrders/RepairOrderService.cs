using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Application.Customers;
using RepairHub.Application.Devices;
using RepairHub.Domain.Customers;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;

namespace RepairHub.Application.RepairOrders;

/// <summary>
/// Changes to repair orders. Each operation saves the order and its history events with a single SaveChanges
/// (one transaction); creation additionally runs inside an explicit transaction together with number generation
/// and any new customer/device. Updates carry the version the client read and fail with 409 when it is stale.
/// </summary>
public sealed class RepairOrderService(
    IAppDbContext db,
    ICurrentUser currentUser,
    CustomerService customers,
    DeviceService devices,
    RepairOrderQueries queries,
    TimeProvider time,
    ILogger<RepairOrderService> logger)
{
    public async Task<RepairOrderDetailsDto> CreateAsync(CreateRepairOrderRequest request, CancellationToken ct)
    {
        var actor = currentUser.RequireActor();
        if (!RepairOrderPermissions.CanCreate(actor)) throw new ForbiddenException();

        var now = time.GetUtcNow();
        var workshop = await db.LoadCurrentWorkshopAsync(ct);
        var today = WorkshopTime.Today(now, workshop.TimeZone);
        EnsureNotInPast(request.ExpectedCompletionDate, today);
        if (request.AssignedTechnicianId is { } technicianId)
            await EnsureAssignableAsync(technicianId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var customer = request.NewCustomer is { } newCustomer
            ? await customers.CreateEntityAsync(actor, newCustomer, ct)
            : await LoadCustomerForNewOrderAsync(request.CustomerId!.Value, ct);

        var device = request.NewDevice is { } newDevice
            ? devices.CreateEntity(actor, customer.Id, newDevice)
            : await LoadDeviceForNewOrderAsync(request.DeviceId!.Value, customer.Id, ct);

        // The numbering year is the workshop-local year of creation (31 Dec 23:30 in Almaty is still that year).
        var sequence = await db.NextRepairOrderSequenceAsync(actor.WorkshopId, today.Year, ct);

        var (order, events) = RepairOrder.Create(
            actor.WorkshopId, today.Year, sequence,
            customer.Id, CustomerSnapshot.Of(customer),
            device.Id, DeviceSnapshot.Of(device),
            request.Problem, request.Condition, request.Accessories,
            request.AssignedTechnicianId, request.ExpectedCompletionDate, request.EstimatedPrice,
            workshop.Currency, actor.UserId, now);

        db.RepairOrders.Add(order);
        db.RepairOrderEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Repair order {RepairOrderId} ({Number}) created by {ActorId}", order.Id, order.Number, actor.UserId);
        return await queries.GetAsync(order.Id, ct);
    }

    public Task<RepairOrderDetailsDto> UpdateIntakeAsync(Guid id, UpdateIntakeRequest request, CancellationToken ct) =>
        MutateAsync(id, request.Version, (actor, order, now) =>
        {
            Require(RepairOrderPermissions.CanEditIntake(actor, order), order);
            return Task.FromResult(order.UpdateIntake(request.Problem, request.Condition, request.Accessories, actor.UserId, now));
        }, ct);

    public Task<RepairOrderDetailsDto> UpdateDiagnosticsAsync(Guid id, UpdateDiagnosticsRequest request, CancellationToken ct) =>
        MutateAsync(id, request.Version, (actor, order, now) =>
        {
            Require(RepairOrderPermissions.CanEditDiagnostics(actor, order), order);
            return Task.FromResult(order.UpdateDiagnostics(request.Diagnostics, actor.UserId, now));
        }, ct);

    public Task<RepairOrderDetailsDto> UpdateTermsAsync(Guid id, UpdateTermsRequest request, CancellationToken ct) =>
        MutateAsync(id, request.Version, async (actor, order, now) =>
        {
            Require(RepairOrderPermissions.CanEditTerms(actor, order), order);
            if (request.ExpectedCompletionDate != order.ExpectedCompletionDate)
            {
                var workshop = await db.LoadCurrentWorkshopAsync(ct);
                EnsureNotInPast(request.ExpectedCompletionDate, WorkshopTime.Today(now, workshop.TimeZone));
            }
            return order.UpdateTerms(request.EstimatedPrice, request.FinalPrice, request.ExpectedCompletionDate, actor.UserId, now);
        }, ct);

    public Task<RepairOrderDetailsDto> CorrectSnapshotAsync(Guid id, CorrectSnapshotRequest request, CancellationToken ct) =>
        MutateAsync(id, request.Version, (actor, order, now) =>
        {
            Require(RepairOrderPermissions.CanCorrectSnapshot(actor, order), order);
            var events = new List<RepairOrderEvent>();
            events.AddRange(order.CorrectCustomerInfo(new CustomerSnapshot(request.CustomerName, request.CustomerPhone), actor.UserId, now));
            events.AddRange(order.CorrectDeviceInfo(
                new DeviceSnapshot(request.DeviceType!.Value, request.DeviceBrand, request.DeviceModel,
                    request.DeviceSerialNumber, request.DeviceImei),
                actor.UserId, now));
            return Task.FromResult<IReadOnlyList<RepairOrderEvent>>(events);
        }, ct);

    public Task<RepairOrderDetailsDto> AssignTechnicianAsync(Guid id, AssignTechnicianRequest request, CancellationToken ct) =>
        MutateAsync(id, request.Version, async (actor, order, now) =>
        {
            Require(RepairOrderPermissions.CanAssign(actor, order), order);
            if (request.TechnicianId is { } technicianId && technicianId != order.AssignedTechnicianId)
                await EnsureAssignableAsync(technicianId, ct);
            return order.AssignTechnician(request.TechnicianId, actor.UserId, now);
        }, ct);

    public Task<RepairOrderDetailsDto> ChangeStatusAsync(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        MutateAsync(id, request.Version, (actor, order, now) =>
        {
            var to = request.Status!.Value;
            var transition = RepairOrderWorkflow.Find(order.Status, to);
            if (transition is not null && RepairOrderWorkflow.IsPossible(order, transition)
                && !RepairOrderPermissions.CanChangeStatus(actor, order, transition))
            {
                throw actor.Role == Domain.Users.UserRole.Technician && order.AssignedTechnicianId != actor.UserId
                    ? new ForbiddenException(ErrorCodes.NotAssigned, "Only the assigned technician can change this order.")
                    : new ForbiddenException();
            }
            // Invalid transitions, missing outcome/reason: DomainRuleException (409) from the entity.
            return Task.FromResult(order.ChangeStatus(to, request.Outcome, request.Comment, actor.UserId, now));
        }, ct);

    /// <summary>
    /// "Take order": assigns an UNASSIGNED open order to the caller with one conditional UPDATE, so two technicians
    /// taking the same order at once cannot both succeed — the loser gets 409 concurrency_conflict.
    /// </summary>
    public async Task<RepairOrderDetailsDto> TakeAsync(Guid id, CancellationToken ct)
    {
        var actor = currentUser.RequireActor();
        if (!WorkshopAccess.IsAssignableRole(actor.Role))
            throw new ForbiddenException(ErrorCodes.TechnicianNotAssignable, "Your role cannot be assigned to repair orders.");

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Tenant-filtered UPDATE ... WHERE id = @id AND assigned_technician_id IS NULL AND status NOT IN (closed).
        // A concurrent taker blocks on the row lock and then re-evaluates the WHERE clause, updating nothing.
        var updated = await db.RepairOrders
            .Where(o => o.Id == id && o.AssignedTechnicianId == null && !RepairOrderWorkflow.ClosedStatuses.Contains(o.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.AssignedTechnicianId, (Guid?)actor.UserId)
                .SetProperty(o => o.UpdatedById, actor.UserId)
                .SetProperty(o => o.UpdatedAt, now), ct);

        var order = await db.RepairOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException();
        if (updated == 0)
        {
            if (order.IsClosed)
                throw new Domain.Common.DomainRuleException(RepairOrderErrors.OrderClosed, "The order is closed.");
            throw new ConcurrencyConflictException();
        }

        db.RepairOrderEvents.Add(RepairOrder.TakenEvent(order, actor.UserId, now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Repair order {RepairOrderId} taken by {ActorId}", id, actor.UserId);
        return await queries.GetAsync(id, ct);
    }

    /// <summary>Appends a note to the timeline. Allowed for every role, also on closed orders; notes are never edited.</summary>
    public async Task<RepairOrderEventDto> AddNoteAsync(Guid id, AddNoteRequest request, CancellationToken ct)
    {
        var actor = currentUser.RequireActor();
        var order = await db.RepairOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException();
        if (!RepairOrderPermissions.CanAddNote(actor, order)) throw new ForbiddenException();

        var note = order.AddNote(request.Text, actor.UserId, time.GetUtcNow());
        db.RepairOrderEvents.Add(note);
        await db.SaveChangesAsync(ct);
        return (await queries.HistoryAsync(id, ct)).Single(e => e.Id == note.Id);
    }

    private delegate Task<IReadOnlyList<RepairOrderEvent>> Mutation(Actor actor, RepairOrder order, DateTimeOffset now);

    private async Task<RepairOrderDetailsDto> MutateAsync(Guid id, uint? version, Mutation mutation, CancellationToken ct)
    {
        var actor = currentUser.RequireActor();
        var order = await db.RepairOrders.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException();

        // The client's version must match the current row; EF additionally checks xmin in the UPDATE,
        // which catches a change committed between this read and the save.
        if (version != order.Version) throw new ConcurrencyConflictException();

        var events = await mutation(actor, order, time.GetUtcNow());
        if (events.Count > 0)
        {
            db.RepairOrderEvents.AddRange(events);
            await db.SaveChangesAsync(ct); // order UPDATE + event INSERTs in one transaction
        }
        return await queries.GetAsync(id, ct);
    }

    /// <summary>403, except for closed orders where the more useful answer is 409 order_closed.</summary>
    private static void Require(bool allowed, RepairOrder order)
    {
        if (allowed) return;
        if (order.IsClosed)
            throw new Domain.Common.DomainRuleException(RepairOrderErrors.OrderClosed, "The order is closed and can no longer be changed.");
        throw new ForbiddenException();
    }

    private static void EnsureNotInPast(DateOnly? date, DateOnly today)
    {
        if (date is { } d && d < today)
            throw new InvalidRequestException(ErrorCodes.ExpectedDateInPast, "The expected completion date is in the past.");
    }

    /// <summary>Only active Technicians, Admins and Owners of THIS workshop (tenant filter) can be assigned.</summary>
    private async Task EnsureAssignableAsync(Guid userId, CancellationToken ct)
    {
        var eligible = await db.Users.AnyAsync(
            u => u.Id == userId && u.IsActive && WorkshopAccess.AssignableRoles.Contains(u.Role), ct);
        if (!eligible)
            throw new InvalidRequestException(ErrorCodes.TechnicianNotAssignable, "This user cannot be assigned to repair orders.");
    }

    private async Task<Customer> LoadCustomerForNewOrderAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new ValidationFailedException("customerId", "Customer not found.");
        if (customer.IsArchived)
            throw new InvalidRequestException(ErrorCodes.CustomerArchived, "The customer is archived.");
        return customer;
    }

    private async Task<Device> LoadDeviceForNewOrderAsync(Guid deviceId, Guid customerId, CancellationToken ct)
    {
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId, ct)
            ?? throw new ValidationFailedException("deviceId", "Device not found.");
        if (device.CustomerId != customerId)
            throw new InvalidRequestException(ErrorCodes.DeviceCustomerMismatch, "The device belongs to another customer.");
        if (device.IsArchived)
            throw new InvalidRequestException(ErrorCodes.DeviceArchived, "The device is archived.");
        return device;
    }
}
