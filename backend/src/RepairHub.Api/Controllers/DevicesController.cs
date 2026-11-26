using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepairHub.Api.Auth;
using RepairHub.Application.Common;
using RepairHub.Application.Devices;
using RepairHub.Application.RepairOrders;

namespace RepairHub.Api.Controllers;

/// <summary>Customer devices. Owner, Admin and Receptionist; archiving: Owner and Admin.</summary>
[ApiController]
[Route("api/devices")]
[Authorize(Policy = AuthPolicies.FrontOffice)]
[Produces("application/json")]
public sealed class DevicesController(DeviceService devices, RepairOrderQueries orders) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<DeviceDto>>> List([FromQuery] DeviceListQuery query, CancellationToken ct) =>
        Ok(await devices.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeviceDetailsDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await devices.GetAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DeviceDto>> Create(CreateDeviceRequest request, CancellationToken ct)
    {
        var device = await devices.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = device.Id }, device);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DeviceDto>> Update(Guid id, UpdateDeviceRequest request, CancellationToken ct) =>
        Ok(await devices.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceDto>> Archive(Guid id, CancellationToken ct) =>
        Ok(await devices.ArchiveAsync(id, ct));

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<DeviceDto>> Restore(Guid id, CancellationToken ct) =>
        Ok(await devices.RestoreAsync(id, ct));

    [HttpGet("{id:guid}/repair-orders")]
    public async Task<ActionResult<PagedResult<RepairOrderListItemDto>>> RepairOrders(
        Guid id, [FromQuery] PageQuery page, CancellationToken ct) =>
        Ok(await orders.ListForDeviceAsync(id, page, ct));
}
