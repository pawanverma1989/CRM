namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Stage update/deactivation (PIP-4).</summary>
[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class StagesController(IPipelineService pipelineService) : ControllerBase
{
    /// <summary>Update or deactivate a stage. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("stages/{id:guid}")]
    public async Task<ActionResult<PipelineStageDto>> Update(Guid id, [FromBody] UpdateStageRequest request, CancellationToken ct)
        => Ok(await pipelineService.UpdateStageAsync(id, request, ct));
}
