namespace IdentityApi.Tests.Controllers;
using FluentAssertions;
using IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authService = new();

    private AuthController CreateController()
    {
        var controller = new AuthController(_authService.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private static UserDto MakeUserDto(Guid? orgId = null) => new(
        Guid.NewGuid(), orgId ?? Guid.NewGuid(), null, "user@example.com",
        "Alice", null, null, "sales_rep", true, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        var response = new LoginResponse("access_token", "refresh_token", 900, "Bearer", MakeUserDto());
        _authService.Setup(s => s.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), default))
            .ReturnsAsync(response);

        var controller = CreateController();
        var result = await controller.Login(new LoginRequest("user@example.com", "secret"), default) as OkObjectResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(response);
    }

    [Fact]
    public async Task Login_InvalidCredentials_PropagatesUnauthorizedException()
    {
        _authService.Setup(s => s.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), default))
            .ThrowsAsync(new UnauthorizedException("Invalid credentials."));

        var controller = CreateController();
        await controller.Invoking(c => c.Login(new LoginRequest("x@y.com", "bad"), default))
            .Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Refresh_ValidToken_Returns200WithNewTokens()
    {
        var response = new LoginResponse("new_access", "new_refresh", 900, "Bearer", MakeUserDto());
        _authService.Setup(s => s.RefreshAsync(It.IsAny<RefreshRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), default))
            .ReturnsAsync(response);

        var controller = CreateController();
        var result = await controller.Refresh(new RefreshRequest("old_refresh"), default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(response);
    }

    [Fact]
    public async Task Logout_ValidToken_Returns204()
    {
        _authService.Setup(s => s.LogoutAsync(It.IsAny<LogoutRequest>(), default)).Returns(Task.CompletedTask);

        var controller = CreateController();
        var result = await controller.Logout(new LogoutRequest("token"), default) as NoContentResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(204);
    }
}
