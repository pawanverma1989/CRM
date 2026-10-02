namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>§5: possible duplicates for a draft record, before saving (DUP-2, DUP-3).</summary>
[Route("api/customer/v1/duplicates")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class DuplicatesController(IDuplicateService duplicates) : ControllerBase
{
    /// <summary>
    /// Reports both kinds of duplicate: a blocking one that DUP-1 will refuse, and the soft
    /// warnings the user may save through (AC-5). Never writes anything.
    /// </summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpPost("check")]
    public async Task<ActionResult<DuplicateCheckResponse>> Check(
        [FromBody] DuplicateCheckRequest request, CancellationToken ct)
        => Ok(await duplicates.CheckAsync(request, ct));
}
