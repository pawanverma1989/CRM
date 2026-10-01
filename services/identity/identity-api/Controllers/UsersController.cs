namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Interfaces;
using IdentityApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/users")]
[ApiController]
[Authorize]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await userService.ListAsync(User.GetOrganizationId(), ct));

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var user = await userService.CreateAsync(request, User.GetOrganizationId(), User.GetUserId(), ct);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await userService.GetByIdAsync(id, User.GetOrganizationId(), ct));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
        => Ok(await userService.UpdateAsync(id, request, User.GetOrganizationId(), User.GetUserId(), ct));

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await userService.DeactivateAsync(id, User.GetOrganizationId(), User.GetUserId(), ct);
        return NoContent();
    }
}
