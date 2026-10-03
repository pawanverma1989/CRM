namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class RecycleBinController(IRecycleBinService recycleBin) : ControllerBase
{
    /// <summary>List soft-deleted deals in the recycle bin. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet("recycle-bin")]
    public async Task<ActionResult<PagedResult<RecycleBinDealDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await recycleBin.ListAsync(page, pageSize, ct));

    /// <summary>Restore a soft-deleted deal. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("recycle-bin/{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await recycleBin.RestoreAsync(id, ct);
        return NoContent();
    }
}
