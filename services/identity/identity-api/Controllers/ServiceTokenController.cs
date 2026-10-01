namespace IdentityApi.Controllers;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

[Route("api/identity/v1/oauth")]
[ApiController]
public class ServiceTokenController(IAuthService authService) : ControllerBase
{
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] ServiceTokenRequest request, CancellationToken ct)
        => Ok(await authService.IssueServiceTokenAsync(request, ct));
}
