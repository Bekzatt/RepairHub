using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Domain.Devices;
using RepairHub.Domain.RepairOrders;

namespace RepairHub.Application.Devices;

/// <summary>
/// Customer devices. Front office only (Owner, Admin, Receptionist); archive/restore: Owner, Admin.
/// Editing a device never changes the snapshots stored on existing repair orders.
/// </summary>
public sealed class DeviceService(
    IAppDbContext db,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<DeviceService> logger)
{
    public async Task<PagedResult<DeviceDto>> ListAsync(DeviceListQuery query, CancellationToken ct)
    {
        currentUser.RequireFrontOffice();

        var devices = db.Devices.AsNoTracking();
        if (!query.IncludeArchived) devices = devices.Where(d => !d.IsArchived);
        if (query.CustomerId is { } customerId) devices = devices.Where(d => d.CustomerId == customerId);
        if (query.Type is { } type) devices = devices.Where(d => d.Type == type);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchText.ContainsPattern(query.Search);
            var imei = Device.NormalizeImei(query.Search);
            devices = devices.Where(d =>
                EF.Functions.Like((d.Brand + " " + d.Model).ToLower(), pattern, "\\")
                || EF.Functions.Like(d.SerialNumber!.ToLower(), pattern, "\\")
                || (imei != null && d.Imei!.Contains(imei)));
        }

        var total = await devices.CountAsync(ct);
        var items = await Project(devices.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id))
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);
        return new PagedResult<DeviceDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<DeviceDto>> ListForCustomerAsync(Guid customerId, bool includeArchived, CancellationToken ct)
    {
        currentUser.RequireFrontOffice();
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            throw new NotFoundException();

        var devices = db.Devices.AsNoTracking().Where(d => d.CustomerId == customerId);
        if (!includeArchived) devices = devices.Where(d => !d.IsArchived);
        return await Project(devices.OrderByDescending(d => d.CreatedAt)).ToListAsync(ct);
    }

    public async Task<DeviceDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        currentUser.RequireFrontOffice();
        var device = await Project(db.Devices.AsNoTracking().Where(d => d.Id == id)).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException();

        var orders = db.RepairOrders.Where(o => o.DeviceId == id);
        var orderCount = await orders.CountAsync(ct);
        var openOrderCount = await orders.CountAsync(o => !RepairOrderWorkflow.ClosedStatuses.Contains(o.Status), ct);
        return new DeviceDetailsDto(device, orderCount, openOrderCount);
    }

    public async Task<DeviceDto> CreateAsync(CreateDeviceRequest request, CancellationToken ct)
    {
        var actor = currentUser.RequireFrontOffice();
        var device = await CreateEntityAsync(actor, request.CustomerId!.Value, request, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Device {DeviceId} created by {ActorId}", device.Id, actor.UserId);
        return await GetDtoAsync(device.Id, ct);
    }

    /// <summary>
    /// Validates and adds a new device for an existing customer of this workshop without saving.
    /// Shared with repair order creation.
    /// </summary>
    internal async Task<Device> CreateEntityAsync(Actor actor, Guid customerId, DeviceFields fields, CancellationToken ct)
    {
        // Tenant-filtered: a customer of another workshop is "not found".
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new ValidationFailedException("customerId", "Customer not found.");
        if (customer.IsArchived)
            throw new InvalidRequestException(ErrorCodes.CustomerArchived, "The customer is archived.");

        return CreateEntity(actor, customerId, fields);
    }

    /// <summary>Adds a new device for a customer that the caller has already validated (or is creating).</summary>
    internal Device CreateEntity(Actor actor, Guid customerId, DeviceFields fields)
    {
        var device = Device.Create(
            actor.WorkshopId, customerId, fields.Type!.Value, fields.Brand, fields.Model, fields.SerialNumber,
            fields.Imei, fields.Description, time.GetUtcNow());
        db.Devices.Add(device);
        return device;
    }

    public async Task<DeviceDto> UpdateAsync(Guid id, UpdateDeviceRequest request, CancellationToken ct)
    {
        var actor = currentUser.RequireFrontOffice();
        var device = await LoadAsync(id, ct);
        device.Update(request.Type!.Value, request.Brand, request.Model, request.SerialNumber, request.Imei,
            request.Description, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Device {DeviceId} updated by {ActorId}", device.Id, actor.UserId);
        return await GetDtoAsync(id, ct);
    }

    public async Task<DeviceDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var actor = currentUser.RequireManager();
        var device = await LoadAsync(id, ct);
        if (await db.RepairOrders.AnyAsync(o => o.DeviceId == id && !RepairOrderWorkflow.ClosedStatuses.Contains(o.Status), ct))
            throw new ConflictException(ErrorCodes.HasActiveOrders, "The device has open repair orders.");

        device.Archive(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Device {DeviceId} archived by {ActorId}", device.Id, actor.UserId);
        return await GetDtoAsync(id, ct);
    }

    public async Task<DeviceDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        currentUser.RequireManager();
        var device = await LoadAsync(id, ct);
        device.Restore(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(id, ct);
    }

    private async Task<Device> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException();

    private async Task<DeviceDto> GetDtoAsync(Guid id, CancellationToken ct) =>
        await Project(db.Devices.AsNoTracking().Where(d => d.Id == id)).SingleAsync(ct);

    private IQueryable<DeviceDto> Project(IQueryable<Device> devices) =>
        from d in devices
        join c in db.Customers on d.CustomerId equals c.Id
        select new DeviceDto(
            d.Id, d.CustomerId, c.FullName, d.Type, d.Brand, d.Model, d.SerialNumber, d.Imei, d.Description,
            d.IsArchived, d.CreatedAt, d.UpdatedAt,
            db.RepairOrders.Where(o => o.DeviceId == d.Id).Max(o => (DateTimeOffset?)o.ReceivedAt));
}
