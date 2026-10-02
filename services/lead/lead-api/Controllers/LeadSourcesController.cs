namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/lead/v1/lead-sources")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class LeadSourcesController(ILeadSourceService leadSources) : ControllerBase
{
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet]
    public async Task<ActionResult<List<LeadSourceDto>>> List(
        [FromQuery] bool? isActive = true, CancellationToken ct = default)
        => Ok(await leadSources.ListAsync(isActive, ct));

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<LeadSourceDto>> Create(
        [FromBody] CreateLeadSourceRequest request, CancellationToken ct)
    {
        var source = await leadSources.CreateAsync(request, ct);
        return Created(string.Empty, source);
    }

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<LeadSourceDto>> Update(
        Guid id, [FromBody] UpdateLeadSourceRequest request, CancellationToken ct)
        => Ok(await leadSources.UpdateAsync(id, request, ct));
}
