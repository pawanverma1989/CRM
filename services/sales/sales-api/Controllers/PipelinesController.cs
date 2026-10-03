namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Pipeline management (PIP-1..PIP-4).</summary>
[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class PipelinesController(IPipelineService pipelineService) : ControllerBase
{
    /// <summary>List all pipelines with their stages.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("pipelines")]
    public async Task<ActionResult<List<PipelineDto>>> List(CancellationToken ct)
        => Ok(await pipelineService.ListAsync(ct));

    /// <summary>Get a single pipeline with its stages.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("pipelines/{id:guid}")]
    public async Task<ActionResult<PipelineDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await pipelineService.GetAsync(id, ct));

    /// <summary>Create a new pipeline. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("pipelines")]
    public async Task<ActionResult<PipelineDto>> Create([FromBody] CreatePipelineRequest request, CancellationToken ct)
    {
        var pipeline = await pipelineService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = pipeline.Id }, pipeline);
    }

    /// <summary>Update a pipeline. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("pipelines/{id:guid}")]
    public async Task<ActionResult<PipelineDto>> Update(Guid id, [FromBody] UpdatePipelineRequest request, CancellationToken ct)
        => Ok(await pipelineService.UpdateAsync(id, request, ct));

    /// <summary>Add a stage to a pipeline. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("pipelines/{id:guid}/stages")]
    public async Task<ActionResult<PipelineStageDto>> AddStage(Guid id, [FromBody] CreateStageRequest request, CancellationToken ct)
    {
        var stage = await pipelineService.AddStageAsync(id, request, ct);
        return StatusCode(201, stage);
    }

    /// <summary>Reorder stages within a pipeline. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("pipelines/{id:guid}/stages/reorder")]
    public async Task<IActionResult> ReorderStages(Guid id, [FromBody] ReorderStagesRequest request, CancellationToken ct)
    {
        await pipelineService.ReorderStagesAsync(id, request, ct);
        return NoContent();
    }
}
