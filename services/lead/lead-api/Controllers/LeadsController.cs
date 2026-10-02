namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>Lead CRUD and bulk actions.</summary>
[Route("api/lead/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class LeadsController(ILeadService leadService, IConversionService conversionService) : ControllerBase
{
    /// <summary>List leads visible to the caller with filtering, sorting and pagination.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("leads")]
    public async Task<ActionResult<PagedResult<LeadDto>>> List([FromQuery] LeadListQuery query, CancellationToken ct)
        => Ok(await leadService.ListAsync(query, ct));

    /// <summary>Get a single lead by id. Returns 404 if not visible (never 403, AC-12).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("leads/{id:guid}")]
    public async Task<ActionResult<LeadDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await leadService.GetAsync(id, ct));

    /// <summary>Create a new lead. Must have email OR phone (CAP-1).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPost("leads")]
    public async Task<ActionResult<LeadDto>> Create([FromBody] CreateLeadRequest request, CancellationToken ct)
    {
        var lead = await leadService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = lead.Id }, lead);
    }

    /// <summary>Update a lead. Converted leads are read-only (STA-4). Requires correct version (AC-6).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPatch("leads/{id:guid}")]
    public async Task<ActionResult<LeadDto>> Update(Guid id, [FromBody] UpdateLeadRequest request, CancellationToken ct)
        => Ok(await leadService.UpdateAsync(id, request, ct));

    /// <summary>Soft-delete a lead (moves to recycle bin). Converted leads cannot be deleted (STA-4).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpDelete("leads/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await leadService.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Bulk-assign up to 500 leads to a new owner. Admin or manager only (OWN-2).</summary>
    [Authorize(Policy = Policies.ManagerOrAbove)]
    [HttpPost("leads/assign")]
    public async Task<ActionResult<BulkAssignResult>> Assign([FromBody] BulkAssignRequest request, CancellationToken ct)
        => Ok(await leadService.AssignAsync(request, ct));

    /// <summary>Start the lead-conversion saga (CNV-1..CNV-5).</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPost("leads/{id:guid}/convert")]
    public async Task<ActionResult<ConversionDto>> Convert(
        Guid id, [FromBody] ConversionRequest request, CancellationToken ct)
    {
        var conversion = await conversionService.StartConversionAsync(id, request, ct);
        return Accepted(conversion);
    }
}
