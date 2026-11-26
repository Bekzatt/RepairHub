using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RepairHub.Application.Abstractions;
using RepairHub.Application.Common;
using RepairHub.Domain.Common;
using RepairHub.Domain.Customers;
using RepairHub.Domain.RepairOrders;

namespace RepairHub.Application.Customers;

/// <summary>
/// Customers of the caller's workshop. Front office only (Owner, Admin, Receptionist); archive/restore: Owner, Admin.
/// All queries go through the tenant-filtered DbSets, so another workshop's customer is simply "not found".
/// </summary>
public sealed class CustomerService(
    IAppDbContext db,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<CustomerService> logger)
{
    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerListQuery query, CancellationToken ct)
    {
        currentUser.RequireFrontOffice();

        var customers = db.Customers.AsNoTracking();
        if (!query.IncludeArchived) customers = customers.Where(c => !c.IsArchived);

        if (PhoneNumber.MatchKey(query.Phone) is { } phoneKey)
            customers = customers.Where(c => c.PhoneKey == phoneKey || c.AdditionalPhoneKey == phoneKey);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchText.ContainsPattern(query.Search);
            var digits = SearchText.PhoneDigits(query.Search);
            customers = digits is null
                ? customers.Where(c => EF.Functions.Like(c.FullName.ToLower(), pattern, "\\") || EF.Functions.Like(c.Email!.ToLower(), pattern, "\\"))
                : customers.Where(c => EF.Functions.Like(c.FullName.ToLower(), pattern, "\\")
                    || c.PhoneKey.Contains(digits) || c.AdditionalPhoneKey!.Contains(digits));
        }

        customers = (query.Sort, query.Desc) switch
        {
            (CustomerSort.CreatedAt, false) => customers.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            (CustomerSort.CreatedAt, true) => customers.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id),
            (_, false) => customers.OrderBy(c => c.FullName).ThenBy(c => c.Id),
            (_, true) => customers.OrderByDescending(c => c.FullName).ThenByDescending(c => c.Id),
        };

        var total = await customers.CountAsync(ct);
        var items = await customers
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CustomerDto(c.Id, c.FullName, c.Phone, c.AdditionalPhone, c.Email, c.Notes, c.IsArchived, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<CustomerDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<CustomerDetailsDto> GetAsync(Guid id, CancellationToken ct)
    {
        currentUser.RequireFrontOffice();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException();

        var deviceCount = await db.Devices.CountAsync(d => d.CustomerId == id, ct);
        var orders = db.RepairOrders.Where(o => o.CustomerId == id);
        var orderCount = await orders.CountAsync(ct);
        var openOrderCount = await orders.CountAsync(o => !RepairOrderWorkflow.ClosedStatuses.Contains(o.Status), ct);
        return new CustomerDetailsDto(ToDto(customer), deviceCount, orderCount, openOrderCount);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        var actor = currentUser.RequireFrontOffice();
        var customer = await CreateEntityAsync(actor, request, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Customer {CustomerId} created by {ActorId}", customer.Id, actor.UserId);
        return ToDto(customer);
    }

    /// <summary>
    /// Validates and adds a new customer to the context without saving. Shared with repair order creation,
    /// which saves the customer in the same transaction as the order.
    /// </summary>
    internal async Task<Customer> CreateEntityAsync(Actor actor, CustomerFields fields, CancellationToken ct)
    {
        if (!fields.AllowDuplicatePhone)
            await EnsureNoPhoneDuplicatesAsync(fields, exceptId: null, ct);

        var customer = Customer.Create(
            actor.WorkshopId, fields.FullName, fields.Phone, fields.AdditionalPhone, fields.Email, fields.Notes,
            time.GetUtcNow());
        db.Customers.Add(customer);
        return customer;
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct)
    {
        var actor = currentUser.RequireFrontOffice();
        var customer = await LoadAsync(id, ct);

        if (!request.AllowDuplicatePhone)
            await EnsureNoPhoneDuplicatesAsync(request, exceptId: customer.Id, ct);

        // Existing repair orders keep their own snapshot of the customer's details (historical accuracy).
        customer.Update(request.FullName, request.Phone, request.AdditionalPhone, request.Email, request.Notes, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Customer {CustomerId} updated by {ActorId}", customer.Id, actor.UserId);
        return ToDto(customer);
    }

    public async Task<CustomerDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var actor = currentUser.RequireManager();
        var customer = await LoadAsync(id, ct);

        if (await db.RepairOrders.AnyAsync(o => o.CustomerId == id && !RepairOrderWorkflow.ClosedStatuses.Contains(o.Status), ct))
            throw new ConflictException(ErrorCodes.HasActiveOrders, "The customer has open repair orders.");

        customer.Archive(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Customer {CustomerId} archived by {ActorId}", customer.Id, actor.UserId);
        return ToDto(customer);
    }

    public async Task<CustomerDto> RestoreAsync(Guid id, CancellationToken ct)
    {
        currentUser.RequireManager();
        var customer = await LoadAsync(id, ct);
        customer.Restore(time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return ToDto(customer);
    }

    private async Task<Customer> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Customers.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException();

    /// <summary>
    /// Duplicate detection: any customer of this workshop (archived included, so they can be restored instead)
    /// whose main or additional phone has the same match key. Never merges; the caller must confirm.
    /// </summary>
    private async Task EnsureNoPhoneDuplicatesAsync(CustomerFields fields, Guid? exceptId, CancellationToken ct)
    {
        var keys = new[] { PhoneNumber.MatchKey(fields.Phone), PhoneNumber.MatchKey(fields.AdditionalPhone) }
            .OfType<string>().Distinct().ToList();
        if (keys.Count == 0) return;

        var matches = await db.Customers.AsNoTracking()
            .Where(c => c.Id != exceptId && (keys.Contains(c.PhoneKey) || keys.Contains(c.AdditionalPhoneKey!)))
            .OrderBy(c => c.IsArchived).ThenBy(c => c.FullName)
            .Take(10)
            .Select(c => new CustomerMatchDto(c.Id, c.FullName, c.Phone, c.AdditionalPhone, c.IsArchived))
            .ToListAsync(ct);
        if (matches.Count == 0) return;

        throw new ConflictException(ErrorCodes.CustomerPhoneExists, "A customer with this phone number already exists.")
        {
            Details = new Dictionary<string, object?> { ["matches"] = matches },
        };
    }

    internal static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.FullName, c.Phone, c.AdditionalPhone, c.Email, c.Notes, c.IsArchived, c.CreatedAt, c.UpdatedAt);
}
