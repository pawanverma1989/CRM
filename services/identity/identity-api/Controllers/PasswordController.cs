namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/v1/password")]
[ApiController]
public class PasswordController(IAuthService authService) : ControllerBase
{
    [HttpPost("forgot")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        await authService.ForgotPasswordAsync(request, ct);
        return NoContent();
    }

    [HttpPost("reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await authService.ResetPasswordAsync(request, ct);
        return NoContent();
    }
}
