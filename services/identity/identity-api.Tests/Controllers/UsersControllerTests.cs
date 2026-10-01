namespace IdentityApi.Tests.Controllers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class UsersControllerTests
{
    private readonly Mock<IUserService> _userService = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    private UsersController CreateController()
    {
        var controller = new UsersController(_userService.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = MakeClaimsPrincipal(_userId, _orgId)
            }
        };
        return controller;
    }

    private static ClaimsPrincipal MakeClaimsPrincipal(Guid userId, Guid orgId, string role = "admin")
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("organization_id", orgId.ToString()),
            new Claim("role", role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private UserDto MakeUserDto(Guid? id = null) => new(
        id ?? Guid.NewGuid(), _orgId, null, "user@example.com",
        "Alice", null, null, "sales_rep", true, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task List_Returns200WithUsers()
    {
        var users = new List<UserDto> { MakeUserDto(), MakeUserDto() };
        _userService.Setup(s => s.ListAsync(_orgId, default)).ReturnsAsync(users);

        var result = await CreateController().List(default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().BeEquivalentTo(users);
    }

    [Fact]
    public async Task GetById_ExistingUser_Returns200()
    {
        var dto = MakeUserDto();
        _userService.Setup(s => s.GetByIdAsync(dto.Id, _orgId, default)).ReturnsAsync(dto);

        var result = await CreateController().GetById(dto.Id, default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(dto);
    }

    [Fact]
    public async Task GetById_NonExistingUser_PropagatesNotFoundException()
    {
        var id = Guid.NewGuid();
        _userService.Setup(s => s.GetByIdAsync(id, _orgId, default)).ThrowsAsync(new NotFoundException("not found"));

        await CreateController().Invoking(c => c.GetById(id, default))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201()
    {
        var dto = MakeUserDto();
        var request = new CreateUserRequest("new@example.com", "P@ss1!", "Bob", null, null, "sales_rep", null);
        _userService.Setup(s => s.CreateAsync(request, _orgId, _userId, default)).ReturnsAsync(dto);

        var result = await CreateController().Create(request, default) as CreatedAtActionResult;

        result!.StatusCode.Should().Be(201);
        result.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Deactivate_ExistingUser_Returns204()
    {
        var id = Guid.NewGuid();
        _userService.Setup(s => s.DeactivateAsync(id, _orgId, _userId, default)).Returns(Task.CompletedTask);

        var result = await CreateController().Deactivate(id, default) as NoContentResult;

        result!.StatusCode.Should().Be(204);
    }
}
