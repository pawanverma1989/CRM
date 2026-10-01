namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Organizations;
using IdentityApi.Application.Interfaces;
using IdentityApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/organizations")]
[ApiController]
public class OrganizationsController(IOrganizationService orgService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrganizationRequest request, CancellationToken ct)
    {
        var (org, _) = await orgService.CreateWithAdminAsync(request, ct);
        return CreatedAtAction(nameof(GetMe), null, org);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var orgId = User.GetOrganizationId();
        var org = await orgService.GetByIdAsync(orgId, ct);
        return Ok(org);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateOrganizationRequest request, CancellationToken ct)
    {
        var orgId = User.GetOrganizationId();
        var actorId = User.GetUserId();
        var org = await orgService.UpdateAsync(orgId, request, actorId, ct);
        return Ok(org);
    }
}
