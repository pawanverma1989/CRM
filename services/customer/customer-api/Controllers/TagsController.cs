namespace CustomerApi.Controllers;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>§5: tag suggestions while typing (TAG-3).</summary>
[Route("api/customer/v1/tags")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class TagsController(
    ILookupRepository lookups,
    IRequestContext ctx,
    IOptions<CustomerSettings> settings) : ControllerBase
{
    /// <summary>TAG-3: tags already used on companies and contacts in this organization.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<string>>> List(
        [FromQuery] string? prefix,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var max = Math.Clamp(limit ?? settings.Value.DefaultPageSize, 1, settings.Value.MaxPageSize);
        return Ok(await lookups.TagSuggestionsAsync(ctx.OrganizationId, prefix, max, ct));
    }
}
