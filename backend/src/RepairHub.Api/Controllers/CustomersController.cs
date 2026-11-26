using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepairHub.Api.Auth;
using RepairHub.Application.Common;
using RepairHub.Application.Customers;
using RepairHub.Application.Devices;
using RepairHub.Application.RepairOrders;

namespace RepairHub.Api.Controllers;

/// <summary>Customers of the caller's workshop. Owner, Admin and Receptionist; archiving: Owner and Admin.</summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = AuthPolicies.FrontOffice)]
[Produces("application/json")]
public sealed class CustomersController(
    CustomerService customers, DeviceService devices, RepairOrderQueries orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List([FromQuery] CustomerListQuery query, CancellationToken ct) =>
        Ok(await customers.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailsDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await customers.GetAsync(id, ct));

    /// <summary>409 customer_phone_exists (with "matches") unless allowDuplicatePhone is true.</summary>
    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, UpdateCustomerRequest request, CancellationToken ct) =>
        Ok(await customers.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Archive(Guid id, CancellationToken ct) =>
        Ok(await customers.ArchiveAsync(id, ct));

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<CustomerDto>> Restore(Guid id, CancellationToken ct) =>
        Ok(await customers.RestoreAsync(id, ct));

    [HttpGet("{id:guid}/devices")]
    public async Task<ActionResult<IReadOnlyList<DeviceDto>>> Devices(Guid id, [FromQuery] bool includeArchived, CancellationToken ct) =>
        Ok(await devices.ListForCustomerAsync(id, includeArchived, ct));

    [HttpGet("{id:guid}/repair-orders")]
    public async Task<ActionResult<PagedResult<RepairOrderListItemDto>>> RepairOrders(
        Guid id, [FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await orders.ListForCustomerAsync(id, page, ct));
}
