namespace SalesApi.Controllers;
using SalesApi.Application.DTOs;
using SalesApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/sales/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class CustomFieldsController(ICustomFieldService customFieldService) : ControllerBase
{
    /// <summary>List custom field definitions.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("custom-fields")]
    public async Task<ActionResult<List<CustomFieldDefinitionDto>>> List([FromQuery] string entityType = "deal", CancellationToken ct = default)
        => Ok(await customFieldService.ListAsync(entityType, ct));

    /// <summary>Create a custom field definition. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("custom-fields")]
    public async Task<ActionResult<CustomFieldDefinitionDto>> Create([FromBody] CreateCustomFieldRequest request, CancellationToken ct)
    {
        var field = await customFieldService.CreateAsync(request, ct);
        return StatusCode(201, field);
    }

    /// <summary>Update a custom field definition. Admin only.</summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPatch("custom-fields/{id:guid}")]
    public async Task<ActionResult<CustomFieldDefinitionDto>> Update(Guid id, [FromBody] UpdateCustomFieldRequest request, CancellationToken ct)
        => Ok(await customFieldService.UpdateAsync(id, request, ct));
}
