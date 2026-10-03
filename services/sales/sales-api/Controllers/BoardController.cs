namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Kanban board view (BRD-1..BRD-5).</summary>
[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class BoardController(IBoardService boardService) : ControllerBase
{
    /// <summary>Get the kanban board for a pipeline, grouped by open stage with totals and weighted forecast.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("board")]
    public async Task<ActionResult<BoardResponse>> GetBoard([FromQuery] Guid pipelineId, CancellationToken ct)
        => Ok(await boardService.GetBoardAsync(pipelineId, ct));
}
