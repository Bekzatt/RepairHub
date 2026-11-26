using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RepairHub.Api.Auth;
using RepairHub.Application.Common;
using RepairHub.Application.Users;

namespace RepairHub.Api.Controllers;

/// <summary>Staff management. Owner and Admin only; finer role rules are enforced by <see cref="UserService"/>.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = AuthPolicies.ManageUsers)]
[Produces("application/json")]
public sealed class UsersController(UserService users) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> List([FromQuery] UserListQuery query, CancellationToken ct) =>
        Ok(await users.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken ct) => Ok(await users.GetAsync(id, ct));

    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        Ok(await users.UpdateAsync(id, request, ct));

    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Deactivate(Guid id, CancellationToken ct) =>
        Ok(await users.DeactivateAsync(id, ct));

    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<UserDto>> Activate(Guid id, CancellationToken ct) =>
        Ok(await users.ActivateAsync(id, ct));

    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }
}
