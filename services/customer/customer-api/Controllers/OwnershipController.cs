namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>§5: ownership — reassignment, the deactivated-user queue and the owner picker.</summary>
[Route("api/customer/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class OwnershipController(IOwnershipService ownership) : ControllerBase
{
    /// <summary>OWN-2: change the owner of up to <c>Customer:MaxBulkRecords</c> records; one event per record.</summary>
    [Authorize(Policy = Policies.ManagerOrAbove)]
    [HttpPost("reassign")]
    public async Task<ActionResult<BulkActionResultDto>> Reassign(
        [FromBody] ReassignRequest request, CancellationToken ct)
        => Ok(await ownership.ReassignAsync(request, ct));

    /// <summary>OWN-3: records whose owner was deactivated in Identity.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet("reassignment-queue")]
    public async Task<ActionResult<IReadOnlyList<ReassignmentQueueItemDto>>> Queue(CancellationToken ct)
        => Ok(await ownership.QueueAsync(ct));

    /// <summary>
    /// The owners a list or an owner picker can show, read from the local <c>user_refs</c> copy.
    /// Any signed-in user may call it — a name is not a permission.
    /// </summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet("owners")]
    public async Task<ActionResult<IReadOnlyList<OwnerDto>>> Owners(
        [FromQuery] bool includeInactive, CancellationToken ct)
        => Ok(await ownership.OwnersAsync(includeInactive, ct));
}
