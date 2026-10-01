namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Teams;
using IdentityApi.Application.Interfaces;
using IdentityApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/teams")]
[ApiController]
[Authorize]
public class TeamsController(ITeamService teamService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await teamService.ListAsync(User.GetOrganizationId(), ct));

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTeamRequest request, CancellationToken ct)
    {
        var team = await teamService.CreateAsync(request, User.GetOrganizationId(), User.GetUserId(), ct);
        return CreatedAtAction(nameof(GetById), new { id = team.Id }, team);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await teamService.GetByIdAsync(id, User.GetOrganizationId(), ct));

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeamRequest request, CancellationToken ct)
        => Ok(await teamService.UpdateAsync(id, request, User.GetOrganizationId(), User.GetUserId(), ct));

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await teamService.DeleteAsync(id, User.GetOrganizationId(), User.GetUserId(), ct);
        return NoContent();
    }
}
