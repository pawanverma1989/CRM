namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/lead/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class RecycleBinController(IRecycleBinService recycleBin) : ControllerBase
{
    /// <summary>List soft-deleted leads in the recycle bin (admin only).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet("recycle-bin")]
    public async Task<ActionResult<PagedResult<RecycleBinLeadDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await recycleBin.ListAsync(page, pageSize, ct));

    /// <summary>Restore a soft-deleted lead (admin only).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("recycle-bin/{id:guid}/restore")]
    public async Task<ActionResult<LeadDto>> Restore(Guid id, CancellationToken ct)
        => Ok(await recycleBin.RestoreAsync(id, ct));
}
