namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/lead/v1/disqualify-reasons")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class DisqualifyReasonsController(IDisqualifyReasonService disqualifyReasons) : ControllerBase
{
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet]
    public async Task<ActionResult<List<DisqualifyReasonDto>>> List(
        [FromQuery] bool? isActive = true, CancellationToken ct = default)
        => Ok(await disqualifyReasons.ListAsync(isActive, ct));

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<DisqualifyReasonDto>> Create(
        [FromBody] CreateDisqualifyReasonRequest request, CancellationToken ct)
    {
        var reason = await disqualifyReasons.CreateAsync(request, ct);
        return Created(string.Empty, reason);
    }

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<DisqualifyReasonDto>> Update(
        Guid id, [FromBody] UpdateDisqualifyReasonRequest request, CancellationToken ct)
        => Ok(await disqualifyReasons.UpdateAsync(id, request, ct));
}
