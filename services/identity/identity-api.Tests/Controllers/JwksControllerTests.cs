namespace IdentityApi.Tests.Controllers;
using FluentAssertions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Moq;

public class JwksControllerTests
{
    private readonly Mock<ITokenService> _tokenService = new();

    private JwksController CreateController()
    {
        var controller = new JwksController(_tokenService.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public void GetJwks_Returns200WithKeySet()
    {
        var jwks = new JsonWebKeySet();
        _tokenService.Setup(t => t.GetPublicKeySet()).Returns(jwks);

        var result = CreateController().GetJwks() as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(jwks);
    }

    [Fact]
    public void GetJwks_SetsCacheControlHeader()
    {
        _tokenService.Setup(t => t.GetPublicKeySet()).Returns(new JsonWebKeySet());

        var controller = CreateController();
        controller.GetJwks();

        controller.Response.Headers.CacheControl.ToString().Should().Contain("public");
        controller.Response.Headers.CacheControl.ToString().Should().Contain("max-age=3600");
    }
}
