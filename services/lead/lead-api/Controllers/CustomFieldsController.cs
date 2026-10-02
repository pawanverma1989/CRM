namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/lead/v1/custom-fields")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class CustomFieldsController(ICustomFieldService customFields) : ControllerBase
{
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet]
    public async Task<ActionResult<List<CustomFieldDefinitionDto>>> List(
        [FromQuery] string entityType = "lead", CancellationToken ct = default)
        => Ok(await customFields.ListAsync(entityType, ct));

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<CustomFieldDefinitionDto>> Create(
        [FromBody] CreateCustomFieldRequest request, CancellationToken ct)
    {
        var field = await customFields.CreateAsync(request, ct);
        return Created(string.Empty, field);
    }

    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<CustomFieldDefinitionDto>> Update(
        Guid id, [FromBody] UpdateCustomFieldRequest request, CancellationToken ct)
        => Ok(await customFields.UpdateAsync(id, request, ct));
}
