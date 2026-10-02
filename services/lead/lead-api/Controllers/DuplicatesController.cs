namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/lead/v1")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class DuplicatesController(ILeadService leadService) : ControllerBase
{
    /// <summary>Check for potential duplicate leads by email or phone.</summary>
    [Authorize(Policy = Policies.AnyAuthenticatedUser)]
    [HttpPost("leads/duplicates/check")]
    public async Task<ActionResult<DuplicateCheckResult>> Check(
        [FromBody] DuplicateCheckRequest request, CancellationToken ct)
        => Ok(await leadService.DuplicateCheckAsync(request, ct));
}
