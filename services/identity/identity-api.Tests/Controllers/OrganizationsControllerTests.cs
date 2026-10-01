namespace IdentityApi.Tests.Controllers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using IdentityApi.Application.DTOs.Organizations;
using IdentityApi.Application.DTOs.Users;
using IdentityApi.Application.Interfaces;
using IdentityApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class OrganizationsControllerTests
{
    private readonly Mock<IOrganizationService> _orgService = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    private OrganizationsController CreateController()
    {
        var controller = new OrganizationsController(_orgService.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = MakeClaimsPrincipal(_userId, _orgId)
            }
        };
        return controller;
    }

    private static ClaimsPrincipal MakeClaimsPrincipal(Guid userId, Guid orgId)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("organization_id", orgId.ToString()),
            new Claim("role", "admin")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private OrganizationDto MakeOrgDto() => new(
        _orgId, "Acme Corp", "INR", "Asia/Kolkata", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Create_ValidRequest_Returns201()
    {
        var orgDto = MakeOrgDto();
        var adminDto = new UserDto(Guid.NewGuid(), _orgId, null, "admin@acme.com", "Admin", null, null, "admin", true, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var request = new CreateOrganizationRequest("Acme Corp", "INR", "Asia/Kolkata", "admin@acme.com", "P@ss1!", "Admin", null);

        _orgService.Setup(s => s.CreateWithAdminAsync(request, default)).ReturnsAsync((orgDto, adminDto));

        var result = await CreateController().Create(request, default) as CreatedAtActionResult;

        result!.StatusCode.Should().Be(201);
        result.Value.Should().Be(orgDto);
    }

    [Fact]
    public async Task GetMe_ReturnsCurrentOrg()
    {
        var orgDto = MakeOrgDto();
        _orgService.Setup(s => s.GetByIdAsync(_orgId, default)).ReturnsAsync(orgDto);

        var result = await CreateController().GetMe(default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(orgDto);
    }

    [Fact]
    public async Task UpdateMe_ValidRequest_Returns200()
    {
        var orgDto = new OrganizationDto(_orgId, "New Name", "USD", "America/New_York", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var request = new UpdateOrganizationRequest("New Name", "USD", "America/New_York");

        _orgService.Setup(s => s.UpdateAsync(_orgId, request, _userId, default)).ReturnsAsync(orgDto);

        var result = await CreateController().UpdateMe(request, default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(orgDto);
    }
}
