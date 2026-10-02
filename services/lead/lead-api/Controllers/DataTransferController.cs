namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Bulk import and export for the data-transfer service (ServiceOnly policy).</summary>
[Route("api/lead/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class DataTransferController(IBulkImportService bulkImport, IExportService export) : ControllerBase
{
    /// <summary>
    /// Bulk upsert up to 1000 leads (service token only).
    /// Matches by email then phone_normalized; creates if no match, updates if found.
    /// </summary>
    [Authorize(Policy = Policies.ServiceOnly)]
    [HttpPost("bulk-upsert")]
    public async Task<ActionResult<BulkUpsertResponse>> BulkUpsert(
        [FromBody] IReadOnlyList<CreateLeadRequest> rows, CancellationToken ct)
        => Ok(await bulkImport.UpsertAsync(rows, ct));

    /// <summary>Paged export for the data-transfer service (service token only).</summary>
    [Authorize(Policy = Policies.ServiceOnly)]
    [HttpGet("export")]
    public async Task<ActionResult<PagedResult<LeadDto>>> Export(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        [FromQuery] DateTimeOffset? updatedSince = null,
        CancellationToken ct = default)
        => Ok(await export.ExportAsync(page, pageSize, updatedSince, ct));
}
