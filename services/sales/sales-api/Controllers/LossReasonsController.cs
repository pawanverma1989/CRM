namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class LossReasonsController(ILossReasonService lossReasonService) : ControllerBase
{
    /// <summary>List all loss reasons.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("loss-reasons")]
    public async Task<ActionResult<List<LossReasonDto>>> List(CancellationToken ct)
        => Ok(await lossReasonService.ListAsync(ct));

    /// <summary>Create a loss reason. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("loss-reasons")]
    public async Task<ActionResult<LossReasonDto>> Create([FromBody] CreateLossReasonRequest request, CancellationToken ct)
    {
        var reason = await lossReasonService.CreateAsync(request, ct);
        return StatusCode(201, reason);
    }

    /// <summary>Update a loss reason. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("loss-reasons/{id:guid}")]
    public async Task<ActionResult<LossReasonDto>> Update(Guid id, [FromBody] UpdateLossReasonRequest request, CancellationToken ct)
        => Ok(await lossReasonService.UpdateAsync(id, request, ct));
}
