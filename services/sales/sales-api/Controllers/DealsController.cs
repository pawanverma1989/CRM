namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Deal CRUD and lifecycle actions (DL-1..DL-6, WL-1..WL-3).</summary>
[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class DealsController(IDealService dealService) : ControllerBase
{
    /// <summary>List deals visible to the caller with filtering, sorting and pagination.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("deals")]
    public async Task<ActionResult<PagedResult<DealDto>>> List([FromQuery] DealListQuery query, CancellationToken ct)
        => Ok(await dealService.ListAsync(query, ct));

    /// <summary>Get a single deal by id with contacts and stage history. Returns 404 if not visible.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("deals/{id:guid}")]
    public async Task<ActionResult<DealDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await dealService.GetAsync(id, ct));

    /// <summary>Create a new deal.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpPost("deals")]
    public async Task<ActionResult<DealDto>> Create([FromBody] CreateDealRequest request, CancellationToken ct)
    {
        var deal = await dealService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = deal.Id }, deal);
    }

    /// <summary>Update a deal. Must include current version (DL-5 optimistic locking).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPatch("deals/{id:guid}")]
    public async Task<ActionResult<DealDto>> Update(Guid id, [FromBody] UpdateDealRequest request, CancellationToken ct)
        => Ok(await dealService.UpdateAsync(id, request, ct));

    /// <summary>Move a deal to a different stage within the same pipeline.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPost("deals/{id:guid}/move")]
    public async Task<ActionResult<DealDto>> Move(Guid id, [FromBody] MoveDealRequest request, CancellationToken ct)
        => Ok(await dealService.MoveAsync(id, request, ct));

    /// <summary>Mark a deal as won. Optionally supply a close date (WL-1).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPost("deals/{id:guid}/won")]
    public async Task<ActionResult<DealDto>> MarkWon(Guid id, [FromBody] MarkWonRequest request, CancellationToken ct)
        => Ok(await dealService.MarkWonAsync(id, request, ct));

    /// <summary>Mark a deal as lost. Requires loss_reason_id (WL-2).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPost("deals/{id:guid}/lost")]
    public async Task<ActionResult<DealDto>> MarkLost(Guid id, [FromBody] MarkLostRequest request, CancellationToken ct)
        => Ok(await dealService.MarkLostAsync(id, request, ct));

    /// <summary>Reopen a closed deal. Clears closed_at and loss fields, moves to specified open stage (WL-3).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPost("deals/{id:guid}/reopen")]
    public async Task<ActionResult<DealDto>> Reopen(Guid id, [FromBody] ReopenDealRequest request, CancellationToken ct)
        => Ok(await dealService.ReopenAsync(id, request, ct));

    /// <summary>Replace the full contact list for a deal (DL-2).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPut("deals/{id:guid}/contacts")]
    public async Task<ActionResult<DealDto>> ReplaceContacts(Guid id, [FromBody] ReplaceDealContactsRequest request, CancellationToken ct)
        => Ok(await dealService.ReplaceContactsAsync(id, request, ct));

    /// <summary>Soft-delete a deal (moves to recycle bin, DEL-1).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpDelete("deals/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await dealService.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Bulk-reassign up to 500 deals. Admin or manager only (OWN-2).</summary>
    [Authorize(Policy = Policies.ManagerOrAbove)]
    [HttpPost("deals/reassign")]
    public async Task<ActionResult<BulkReassignResult>> Reassign([FromBody] BulkReassignRequest request, CancellationToken ct)
        => Ok(await dealService.ReassignAsync(request, ct));
}
