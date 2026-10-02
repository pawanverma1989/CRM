namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>§5: companies. Every action needs a token; <c>organization_id</c> comes only from it (NFR-6).</summary>
[Route("api/customer/v1/companies")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class CompaniesController(ICompanyService companies, IMergeService merges) : ControllerBase
{
    /// <summary>DUP-4..DUP-6: merge two companies. Cannot be undone.</summary>
    [Authorize(Policy = Policies.ManagerOrAbove)]
    [HttpPost("merge")]
    public async Task<ActionResult<MergeResultDto>> Merge([FromBody] MergeRequest request, CancellationToken ct)
        => Ok(await merges.MergeCompaniesAsync(request, ct));

    /// <summary>LST-1..LST-4: list, filter, sort and quick-search the companies the caller may view.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<CompanyDto>>> List(
        [FromQuery] CompanyListQuery query, CancellationToken ct)
        => Ok(await companies.ListAsync(query, ct));

    /// <summary>COM-2: a company with its contacts. A company the caller may not view is 404, never 403.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CompanyDetailDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await companies.GetAsync(id, ct));

    /// <summary>COM-1, DUP-1, DUP-3.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpPost]
    public async Task<ActionResult<CompanyDto>> Create(
        [FromBody] CreateCompanyRequest request, CancellationToken ct)
    {
        var company = await companies.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = company.Id }, company);
    }

    /// <summary>COM-3, TAG-1, OWN-2. A stale <c>version</c> is 409 (AC-6).</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<CompanyDto>> Update(
        Guid id, [FromBody] UpdateCompanyRequest request, CancellationToken ct)
        => Ok(await companies.UpdateAsync(id, request, ct));

    /// <summary>DEL-1, COM-5: to the recycle bin, keeping the contacts but unlinking them.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await companies.DeleteAsync(id, ct);
        return NoContent();
    }
}
