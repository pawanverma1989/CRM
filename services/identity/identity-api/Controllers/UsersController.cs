namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Interfaces;
using IdentityApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/v1/users")]
[ApiController]
[Authorize]
public class UsersController(IUserService userService) : ControllerBase
{
    [Authorize(Policy = "ManagerOrAbove")]
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? role,
        [FromQuery] Guid? teamId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        var users = await userService.ListAsync(User.GetOrganizationId(), role, teamId, status, search, ct);
        return Ok(new { data = users, hasMore = false, nextCursor = (string?)null });
    }

    [Authorize(Policy = "ManagerOrAbove")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await userService.GetByIdAsync(id, User.GetOrganizationId(), ct));

    /// <summary>Admin creates an active user with an initial password (alternative to invitation).</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var user = await userService.CreateAsync(request, User.GetOrganizationId(), User.GetUserId(), ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("invitations")]
    public async Task<IActionResult> Invite([FromBody] InviteUserRequest request, CancellationToken ct)
    {
        var user = await userService.InviteAsync(request, User.GetOrganizationId(), User.GetUserId(), ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("invitations/{id:guid}/resend")]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken ct)
    {
        await userService.ResendInvitationAsync(id, User.GetOrganizationId(), User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("invitations/{id:guid}")]
    public async Task<IActionResult> CancelInvitation(Guid id, CancellationToken ct)
    {
        await userService.CancelInvitationAsync(id, User.GetOrganizationId(), ct);
        return NoContent();
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
        => Ok(await userService.UpdateAsync(id, request, User.GetOrganizationId(), User.GetUserId(), ct));

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await userService.DeactivateAsync(id, User.GetOrganizationId(), User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        await userService.ReactivateAsync(id, User.GetOrganizationId(), User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("{id:guid}/logout-all")]
    public async Task<IActionResult> AdminLogoutAll(Guid id, CancellationToken ct)
    {
        await userService.AdminLogoutAllAsync(id, User.GetOrganizationId(), ct);
        return NoContent();
    }
}
