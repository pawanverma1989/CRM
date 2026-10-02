namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The admin-managed value lists behind <c>companies.industry_id</c> and <c>contacts.source_id</c>
/// (§4.1, §4.2). Not in the §5 table, but both FK columns need a list for the UI to offer.
/// </summary>
[Route("api/customer/v1/picklists")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class PicklistsController(IPicklistService picklists) : ControllerBase
{
    /// <summary>
    /// Values for <c>company_industry</c> or <c>contact_source</c>. The first read for an
    /// organization seeds the agreed defaults, so the dropdowns are never empty.
    /// </summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PicklistDto>>> List(
        [FromQuery(Name = "type")] string type,
        [FromQuery] bool includeInactive,
        CancellationToken ct)
        => Ok(await picklists.ListAsync(type, includeInactive, ct));

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<PicklistDto>> Create(
        [FromBody] CreatePicklistValueRequest request, CancellationToken ct)
    {
        var value = await picklists.CreateAsync(request, ct);
        return Created($"/api/customer/v1/picklists/{value.Id}", value);
    }

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<PicklistDto>> Update(
        Guid id, [FromBody] UpdatePicklistValueRequest request, CancellationToken ct)
        => Ok(await picklists.UpdateAsync(id, request, ct));
}
