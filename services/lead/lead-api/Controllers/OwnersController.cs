namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Infrastructure.Context;
using LeadApi.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Owner picker — reads from the local user_refs copy (no synchronous Identity call).</summary>
[Route("api/lead/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class OwnersController(ILookupRepository lookups, IRequestContext ctx) : ControllerBase
{
    /// <summary>The organization's assignable users, from the local user_refs copy.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("owners")]
    public async Task<ActionResult<IReadOnlyList<OwnerDto>>> Owners(
        [FromQuery] bool includeInactive, CancellationToken ct)
    {
        var owners = await lookups.OwnersAsync(ctx.OrganizationId, activeOnly: !includeInactive, ct);
        return Ok(owners.Select(u => new OwnerDto(u.UserId, u.DisplayName, u.IsActive)).ToList());
    }
}
