namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/lead/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class ConversionsController(IConversionService conversionService) : ControllerBase
{
    /// <summary>Get the current state of a conversion saga.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpGet("conversions/{id:guid}")]
    public async Task<ActionResult<ConversionDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await conversionService.GetConversionAsync(id, ct));
}
