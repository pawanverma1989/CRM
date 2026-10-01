namespace IdentityApi.Controllers;
using IdentityApi.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

[Route(".well-known")]
[ApiController]
public class JwksController(ITokenService tokenService) : ControllerBase
{
    [HttpGet("jwks.json")]
    public IActionResult GetJwks()
    {
        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(tokenService.GetPublicKeySet());
    }
}
