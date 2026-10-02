namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// §5: the two endpoints the data transfer service calls for CSV import and export. Both need a
/// service token naming the acting user in <c>X-Acting-User-Id</c>, so the work is done with that
/// user's visibility and ownership (NFR-6). nginx does not rewrite paths, so these are served at
/// <c>/api/customer/v1/bulk-upsert</c> and <c>/api/customer/v1/export</c>.
/// </summary>
[Route("api/customer/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class DataTransferController(
    IBulkImportService bulkImport,
    IExportService export) : ControllerBase
{
    /// <summary>§5 / AC-18: up to <c>Customer:MaxImportRowsPerCall</c> rows, with one result per row.</summary>
    [Authorize(Policy = Policies.ServiceOnly)]
    [HttpPost("bulk-upsert")]
    public async Task<ActionResult<BulkUpsertResponse>> BulkUpsert(
        [FromBody] BulkUpsertRequest request, CancellationToken ct)
        => Ok(await bulkImport.UpsertAsync(request, ct));

    /// <summary>LST-1: paged records for CSV export, respecting the exporting user's visibility.</summary>
    [Authorize(Policy = Policies.ServiceOnly)]
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery(Name = "entity")] string entity,
        [FromQuery] int page,
        [FromQuery] int? pageSize,
        [FromQuery] string? q,
        [FromQuery] Guid? ownerId,
        [FromQuery] DateTimeOffset? updatedFrom,
        CancellationToken ct)
    {
        var entityType = EntityTypes.Require(entity);

        if (page < 1) page = 1;
        if (pageSize is > 0 && pageSize > export.MaxPageSize)
            throw new CustomerValidationException("pageSize",
                $"At most {export.MaxPageSize} records per page.");

        if (entityType == EntityTypes.Company)
        {
            var companies = await export.CompaniesAsync(new CompanyListQuery
            {
                Page = page,
                PageSize = pageSize,
                Q = q,
                OwnerId = ownerId,
                UpdatedFrom = updatedFrom,
                Sort = "created",
                Direction = "asc"
            }, ct);

            return Ok(companies);
        }

        var contacts = await export.ContactsAsync(new ContactListQuery
        {
            Page = page,
            PageSize = pageSize,
            Q = q,
            OwnerId = ownerId,
            UpdatedFrom = updatedFrom,
            Sort = "created",
            Direction = "asc"
        }, ct);

        return Ok(contacts);
    }
}
