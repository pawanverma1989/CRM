namespace CustomerApi.Controllers;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>§5: contacts.</summary>
[Route("api/customer/v1/contacts")]
[ApiController]
[Authorize]
[Produces("application/json")]
public class ContactsController(IContactService contacts, IMergeService merges) : ControllerBase
{
    /// <summary>DUP-4..DUP-6: merge two contacts. Cannot be undone.</summary>
    [Authorize(Policy = Policies.ManagerOrAbove)]
    [HttpPost("merge")]
    public async Task<ActionResult<MergeResultDto>> Merge([FromBody] MergeRequest request, CancellationToken ct)
        => Ok(await merges.MergeContactsAsync(request, ct));

    /// <summary>LST-1..LST-4.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<ContactDto>>> List(
        [FromQuery] ContactListQuery query, CancellationToken ct)
        => Ok(await contacts.ListAsync(query, ct));

    /// <summary>CON-5. A contact the caller may not view is 404, never 403 (AC-7).</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContactDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await contacts.GetAsync(id, ct));

    /// <summary>
    /// CON-1, DUP-1 and CON-7. Called by a signed-in user or, with a service token and
    /// <c>sourceLeadId</c>, by the lead service: a second call for the same lead returns the
    /// existing contact with 200 instead of creating another (AC-16).
    /// </summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpPost]
    public async Task<ActionResult<ContactDto>> Create(
        [FromBody] CreateContactRequest request, CancellationToken ct)
    {
        var (contact, created) = await contacts.CreateAsync(request, ct);

        return created
            ? CreatedAtAction(nameof(GetById), new { id = contact.Id }, contact)
            : Ok(contact);
    }

    /// <summary>CON-2, CON-6, TAG-1. A stale <c>version</c> is 409 (AC-6).</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ContactDto>> Update(
        Guid id, [FromBody] UpdateContactRequest request, CancellationToken ct)
        => Ok(await contacts.UpdateAsync(id, request, ct));

    /// <summary>DEL-1.</summary>
    [Authorize(Policy = Policies.UserOrService)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await contacts.DeleteAsync(id, ct);
        return NoContent();
    }
}
