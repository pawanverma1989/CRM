namespace IdentityApi.Tests.Controllers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using IdentityApi.Application.DTOs.Teams;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

public class TeamsControllerTests
{
    private readonly Mock<ITeamService> _teamService = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    private TeamsController CreateController()
    {
        var controller = new TeamsController(_teamService.Object);
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

    private TeamDto MakeTeamDto(Guid? id = null) => new(
        id ?? Guid.NewGuid(), _orgId, "Sales Team", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task List_Returns200WithTeams()
    {
        var teams = new List<TeamDto> { MakeTeamDto(), MakeTeamDto() };
        _teamService.Setup(s => s.ListAsync(_orgId, default)).ReturnsAsync(teams);

        var result = await CreateController().List(default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().BeEquivalentTo(teams);
    }

    [Fact]
    public async Task GetById_ExistingTeam_Returns200()
    {
        var dto = MakeTeamDto();
        _teamService.Setup(s => s.GetByIdAsync(dto.Id, _orgId, default)).ReturnsAsync(dto);

        var result = await CreateController().GetById(dto.Id, default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(dto);
    }

    [Fact]
    public async Task GetById_NonExistingTeam_PropagatesNotFoundException()
    {
        var id = Guid.NewGuid();
        _teamService.Setup(s => s.GetByIdAsync(id, _orgId, default)).ThrowsAsync(new NotFoundException("not found"));

        await CreateController().Invoking(c => c.GetById(id, default))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201()
    {
        var dto = MakeTeamDto();
        var request = new CreateTeamRequest("Sales Team", null);
        _teamService.Setup(s => s.CreateAsync(request, _orgId, _userId, default)).ReturnsAsync(dto);

        var result = await CreateController().Create(request, default) as CreatedAtActionResult;

        result!.StatusCode.Should().Be(201);
        result.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Update_ExistingTeam_Returns200()
    {
        var id = Guid.NewGuid();
        var dto = MakeTeamDto(id);
        var request = new UpdateTeamRequest("Renamed", null);
        _teamService.Setup(s => s.UpdateAsync(id, request, _orgId, _userId, default)).ReturnsAsync(dto);

        var result = await CreateController().Update(id, request, default) as OkObjectResult;

        result!.StatusCode.Should().Be(200);
        result.Value.Should().Be(dto);
    }

    [Fact]
    public async Task Delete_ExistingTeam_Returns204()
    {
        var id = Guid.NewGuid();
        _teamService.Setup(s => s.DeleteAsync(id, _orgId, _userId, default)).Returns(Task.CompletedTask);

        var result = await CreateController().Delete(id, default) as NoContentResult;

        result!.StatusCode.Should().Be(204);
    }
}
