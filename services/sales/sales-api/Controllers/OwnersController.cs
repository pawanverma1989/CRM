namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class OwnersController(ILookupRepository lookup, IRequestContext ctx) : ControllerBase
{
    /// <summary>List users that can be assigned as deal owners for this organization.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("owners")]
    public async Task<ActionResult<List<OwnerDto>>> List(CancellationToken ct)
    {
        var refs = await lookup.OwnersAsync(ctx.OrganizationId, activeOnly: false, ct);
        return Ok(refs.Select(u => new OwnerDto(u.UserId, u.DisplayName, u.IsActive)).ToList());
    }
}
