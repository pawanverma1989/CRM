namespace LeadApi.Controllers;
using LeadApi.Application.DTOs;
using LeadApi.Application.Services;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Public form submission endpoint — NO [Authorize], rate-limited, body size 20KB (WEB-4).
/// </summary>
[Route("api/lead/v1/public/forms")]
[ApiController]
[Produces("application/json")]
[RequestSizeLimit(20 * 1024)] // 20KB
public class PublicFormsController(IWebFormSubmissionService submissions) : ControllerBase
{
    /// <summary>Submit a lead via a public web form.</summary>
    [HttpPost("{publicKey}/submit")]
    public async Task<ActionResult<PublicFormSubmitResponse>> Submit(
        string publicKey,
        [FromBody] PublicFormSubmitRequest request,
        CancellationToken ct)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await submissions.SubmitAsync(publicKey, request, ipAddress, ct);
        return Ok(result);
    }
}
