namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Data-transfer service endpoints: bulk upsert and export. Service token required.</summary>
[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class DataTransferController(
    IBulkImportService bulkImport,
    IExportService export) : ControllerBase
{
    /// <summary>Bulk create/update deals (≤1000 per call). Service token required.</summary>
    [Authorize(Policy = Policies.ServiceOnly)]
    [HttpPost("bulk-upsert")]
    public async Task<ActionResult<BulkUpsertResponse>> BulkUpsert(
        [FromBody] IReadOnlyList<CreateDealRequest> rows, CancellationToken ct)
        => Ok(await bulkImport.UpsertAsync(rows, ct));

    /// <summary>Export deals as paged rows. Service token required.</summary>
    [Authorize(Policy = Policies.ServiceOnly)]
    [HttpGet("export")]
    public async Task<ActionResult<PagedResult<DealDto>>> Export(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        [FromQuery] DateTimeOffset? updatedSince = null,
        CancellationToken ct = default)
        => Ok(await export.ExportAsync(page, pageSize, updatedSince, ct));
}
