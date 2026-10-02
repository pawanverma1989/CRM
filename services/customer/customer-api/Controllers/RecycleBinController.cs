namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>§5: soft delete, the recycle bin and restore (DEL-1..DEL-4).</summary>
[Route("api/customer/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class RecycleBinController(IRecycleBinService recycleBin) : ControllerBase
{
    /// <summary>DEL-4: delete up to <c>Customer:MaxBulkRecords</c> records the caller may edit.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpPost("bulk-delete")]
    public async Task<ActionResult<BulkActionResultDto>> BulkDelete(
        [FromBody] BulkDeleteRequest request, CancellationToken ct)
        => Ok(await recycleBin.BulkDeleteAsync(request, ct));

    /// <summary>DEL-2: records deleted inside the retention window, which are the restorable ones (AC-15).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet("recycle-bin")]
    public async Task<ActionResult<PagedResult<RecycleBinItemDto>>> List(
        [FromQuery(Name = "type")] string? recordType,
        [FromQuery] int page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => Ok(await recycleBin.ListAsync(recordType, page, pageSize, ct));

    /// <summary>DEL-2: restoring is admin-only in the MVP and is refused when it would break DUP-1 (AC-13, AC-14).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("recycle-bin/{type}/{id:guid}/restore")]
    public async Task<IActionResult> Restore(string type, Guid id, CancellationToken ct)
        => Ok(await recycleBin.RestoreAsync(type, id, ct));
}
