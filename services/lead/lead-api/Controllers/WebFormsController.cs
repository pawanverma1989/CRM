namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

[Route("api/lead/v1/web-forms")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class WebFormsController(IWebFormService webForms, IHttpContextAccessor httpContextAccessor) : ControllerBase
{
    /// <summary>List all web forms (admin only).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet]
    public async Task<ActionResult<List<WebFormDto>>> List(CancellationToken ct)
        => Ok(await webForms.ListAsync(ct));

    /// <summary>Get a web form by id (admin only).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WebFormDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await webForms.GetAsync(id, ct));

    /// <summary>Create a new web form (admin only).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<WebFormDto>> Create([FromBody] CreateWebFormRequest request, CancellationToken ct)
    {
        var form = await webForms.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = form.Id }, form);
    }

    /// <summary>Update a web form (admin only).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<WebFormDto>> Update(
        Guid id, [FromBody] UpdateWebFormRequest request, CancellationToken ct)
        => Ok(await webForms.UpdateAsync(id, request, ct));

    /// <summary>Get the embed snippet for a web form (admin only).</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpGet("{id:guid}/embed")]
    public async Task<ActionResult<WebFormEmbedDto>> GetEmbed(Guid id, CancellationToken ct)
        => Ok(await webForms.GetEmbedAsync(id, httpContextAccessor, ct));
}
