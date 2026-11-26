using Microsoft.AspNetCore.Mvc;
using RepairHub.Application.Common;
using RepairHub.Application.RepairOrders;

namespace RepairHub.Api.Controllers;

/// <summary>
/// Repair orders. Every signed-in staff member can read the orders of their workshop; what each role may change
/// is decided by <see cref="RepairOrderPermissions"/> and the workflow table in the application layer.
/// </summary>
[ApiController]
[Route("api/repair-orders")]
[Produces("application/json")]
public sealed class RepairOrdersController(RepairOrderService orders, RepairOrderQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<RepairOrderListItemDto>>> List(
        [FromQuery] RepairOrderListQuery query, CancellationToken ct) =>
        Ok(await queries.ListAsync(query, ct));

    [HttpGet("summary")]
    public async Task<ActionResult<RepairOrderSummaryDto>> Summary([FromQuery] RepairOrderSummaryQuery query, CancellationToken ct) =>
        Ok(await queries.SummaryAsync(query, ct));

    /// <summary>Active staff that can be assigned: Technicians, Admins and Owners.</summary>
    [HttpGet("assignees")]
    public async Task<ActionResult<IReadOnlyList<AssigneeDto>>> Assignees(CancellationToken ct) =>
        Ok(await queries.AssigneesAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RepairOrderDetailsDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await queries.GetAsync(id, ct));

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<RepairOrderEventDto>>> History(Guid id, CancellationToken ct) =>
        Ok(await queries.HistoryAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<RepairOrderDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RepairOrderDetailsDto>> Create(CreateRepairOrderRequest request, CancellationToken ct)
    {
        var order = await orders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    [HttpPut("{id:guid}/intake")]
    public async Task<ActionResult<RepairOrderDetailsDto>> UpdateIntake(Guid id, UpdateIntakeRequest request, CancellationToken ct) =>
        Ok(await orders.UpdateIntakeAsync(id, request, ct));

    [HttpPut("{id:guid}/diagnostics")]
    public async Task<ActionResult<RepairOrderDetailsDto>> UpdateDiagnostics(Guid id, UpdateDiagnosticsRequest request, CancellationToken ct) =>
        Ok(await orders.UpdateDiagnosticsAsync(id, request, ct));

    [HttpPut("{id:guid}/terms")]
    public async Task<ActionResult<RepairOrderDetailsDto>> UpdateTerms(Guid id, UpdateTermsRequest request, CancellationToken ct) =>
        Ok(await orders.UpdateTermsAsync(id, request, ct));

    [HttpPut("{id:guid}/snapshot")]
    public async Task<ActionResult<RepairOrderDetailsDto>> CorrectSnapshot(Guid id, CorrectSnapshotRequest request, CancellationToken ct) =>
        Ok(await orders.CorrectSnapshotAsync(id, request, ct));

    [HttpPut("{id:guid}/technician")]
    public async Task<ActionResult<RepairOrderDetailsDto>> AssignTechnician(Guid id, AssignTechnicianRequest request, CancellationToken ct) =>
        Ok(await orders.AssignTechnicianAsync(id, request, ct));

    /// <summary>Assigns an unassigned order to the caller. 409 concurrency_conflict if someone else took it first.</summary>
    [HttpPost("{id:guid}/take")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RepairOrderDetailsDto>> Take(Guid id, CancellationToken ct) =>
        Ok(await orders.TakeAsync(id, ct));

    [HttpPost("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RepairOrderDetailsDto>> ChangeStatus(Guid id, ChangeStatusRequest request, CancellationToken ct) =>
        Ok(await orders.ChangeStatusAsync(id, request, ct));

    [HttpPost("{id:guid}/notes")]
    [ProducesResponseType<RepairOrderEventDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RepairOrderEventDto>> AddNote(Guid id, AddNoteRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await orders.AddNoteAsync(id, request, ct));
}
