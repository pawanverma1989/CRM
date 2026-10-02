namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/v1/invitations")]
[ApiController]
public class InvitationsController(IUserService userService) : ControllerBase
{
    [HttpPost("accept")]
    public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationRequest request, CancellationToken ct)
    {
        await userService.AcceptInvitationAsync(request, ct);
        return NoContent();
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
    {
        await userService.ConfirmEmailChangeAsync(request.Token, ct);
        return NoContent();
    }
}
