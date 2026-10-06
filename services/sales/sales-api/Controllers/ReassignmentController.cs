namespace SalesApi.Controllers;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Reassignment queue — open deals of deactivated users (OWN-2).</summary>
[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class ReassignmentController(IReassignmentService reassignment) : ControllerBase
{
    /// <summary>List open deals of deactivated users pending reassignment. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet("reassignment-queue")]
    public async Task<ActionResult<List<ReassignmentQueueItemDto>>> GetQueue(CancellationToken ct)
        => Ok(await reassignment.GetQueueAsync(ct));
}
