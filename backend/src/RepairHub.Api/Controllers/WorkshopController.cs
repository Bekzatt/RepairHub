using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepairHub.Api.Auth;
using RepairHub.Application.Workshops;

namespace RepairHub.Api.Controllers;

/// <summary>The caller's own workshop. There is no way to address another workshop.</summary>
[ApiController]
[Route("api/workshop")]
[Produces("application/json")]
public sealed class WorkshopController(WorkshopService workshops) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WorkshopDto>> Get(CancellationToken ct) => Ok(await workshops.GetCurrentAsync(ct));

    [HttpPut]
    [Authorize(Policy = AuthPolicies.ManageWorkshop)]
    public async Task<ActionResult<WorkshopDto>> Update(UpdateWorkshopRequest request, CancellationToken ct) =>
        Ok(await workshops.UpdateCurrentAsync(request, ct));
}
