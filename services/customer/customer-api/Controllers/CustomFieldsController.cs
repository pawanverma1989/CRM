namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>§5: custom field definitions (CF-1, CF-5, CF-6, CF-7).</summary>
[Route("api/customer/v1/custom-fields")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class CustomFieldsController(ICustomFieldService customFields) : ControllerBase
{
    /// <summary>CF-1: the definitions a form needs. <c>includeInactive</c> is honoured for admins only (CF-5).</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomFieldDefinitionDto>>> List(
        [FromQuery(Name = "entity")] string entity,
        [FromQuery] bool includeInactive,
        CancellationToken ct)
        => Ok(await customFields.ListAsync(entity, includeInactive, ct));

    /// <summary>CF-1, CF-7.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost]
    public async Task<ActionResult<CustomFieldDefinitionDto>> Create(
        [FromBody] CreateCustomFieldRequest request, CancellationToken ct)
    {
        var definition = await customFields.CreateAsync(request, ct);
        return Created($"/api/customer/v1/custom-fields/{definition.Id}", definition);
    }

    /// <summary>CF-5, CF-6: label, options, order and the active flag; never the key or the type.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<CustomFieldDefinitionDto>> Update(
        Guid id, [FromBody] UpdateCustomFieldRequest request, CancellationToken ct)
        => Ok(await customFields.UpdateAsync(id, request, ct));
}
