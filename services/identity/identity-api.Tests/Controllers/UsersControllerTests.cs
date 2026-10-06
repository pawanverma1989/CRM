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

    private UserDto MakeUserDto(Guid? id = null, bool mustChangePassword = false) => new(
        id ?? Guid.NewGuid(), _orgId, null, "user@example.com",
        "Alice", null, null, "sales_rep", "active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, mustChangePassword);

    [Fact]
    public async Task List_Returns200WithUsers()
    {
        var users = new List<UserDto> { MakeUserDto(), MakeUserDto() };
        _userService.Setup(s => s.ListAsync(_orgId, null, null, null, null, default)).ReturnsAsync(users);

        var result = await CreateController().List(null, null, null, null, default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().BeEquivalentTo(new { data = users, hasMore = false, nextCursor = (string?)null });
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
        var dto = MakeUserDto(mustChangePassword: true);
        var request = new CreateUserRequest("new@example.com", "Corr3ct-Horse-Battery", "Bob", null, null, "sales_rep", null);
        _userService.Setup(s => s.CreateAsync(request, _orgId, _userId, default)).ReturnsAsync(dto);

        var result = await CreateController().Create(request, default) as CreatedAtActionResult;

        result!.StatusCode.Should().Be(201);
        result.ActionName.Should().Be(nameof(UsersController.GetById));
        result.RouteValues!["id"].Should().Be(dto.Id);
        result.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Create_UsesOrganizationAndActorFromJwt()
    {
        var request = new CreateUserRequest("new@example.com", "Corr3ct-Horse-Battery", "Bob", null, null, "sales_rep", null);
        _userService.Setup(s => s.CreateAsync(request, It.IsAny<Guid>(), It.IsAny<Guid>(), default)).ReturnsAsync(MakeUserDto());

        await CreateController().Create(request, default);

        _userService.Verify(s => s.CreateAsync(request, _orgId, _userId, default), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateEmail_PropagatesConflictException()
    {
        var request = new CreateUserRequest("dup@example.com", "Corr3ct-Horse-Battery", "Bob", null, null, "sales_rep", null);
        _userService.Setup(s => s.CreateAsync(request, _orgId, _userId, default)).ThrowsAsync(new ConflictException("in use"));

        await CreateController().Invoking(c => c.Create(request, default))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public void Create_IsRestrictedToAdminOnlyPolicy()
    {
        var method = typeof(UsersController).GetMethod(nameof(UsersController.Create))!;

        method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Should().ContainSingle(a => a.Policy == "AdminOnly");
        method.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>()
            .Should().ContainSingle(a => a.Template == null);
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
