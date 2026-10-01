namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.Interfaces;
using IdentityApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/v1/me")]
[ApiController]
[Authorize]
public class MeController(IAuthService authService, IUserService userService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMe(CancellationToken ct)
        => Ok(await userService.GetMeAsync(User.GetUserId(), User.GetOrganizationId(), ct));

    [HttpPatch]
    public async Task<IActionResult> UpdateMe([FromBody] Application.DTOs.Users.UpdateMeRequest request, CancellationToken ct)
        => Ok(await userService.UpdateMeAsync(User.GetUserId(), request, ct));

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await authService.ChangePasswordAsync(User.GetUserId(), request, ct);
        return NoContent();
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
        => Ok(await authService.GetSessionsAsync(User.GetUserId(), ct));

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        await authService.RevokeSessionAsync(User.GetUserId(), sessionId, ct);
        return NoContent();
    }
}
