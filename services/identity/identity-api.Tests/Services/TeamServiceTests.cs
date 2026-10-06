namespace IdentityApi.Tests.Services;
using FluentAssertions;
using IdentityApi.Application.DTOs.Teams;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Services;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Tests.Helpers;
using Moq;

public class TeamServiceTests
{
    private readonly Mock<ITeamRepository> _teamRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IOutboxRepository> _outboxRepo = new();

    private TeamService CreateService()
        => new(_teamRepo.Object, _userRepo.Object, _outboxRepo.Object, DbContextFactory.Create());

    private static Team MakeTeam(Guid? id = null, Guid? orgId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OrganizationId = orgId ?? Guid.NewGuid(),
        Name = "Sales Team",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static User MakeUser(Guid? id = null, Guid? orgId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        OrganizationId = orgId ?? Guid.NewGuid(),
        Email = "manager@example.com",
        PasswordHash = "hash",
        FirstName = "Manager",
        Role = "manager",
        Status = "active",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task ListAsync_ReturnsAllTeamsForOrg()
    {
        var orgId = Guid.NewGuid();
        var teams = new List<Team> { MakeTeam(orgId: orgId), MakeTeam(orgId: orgId) };
        _teamRepo.Setup(r => r.ListByOrganizationAsync(orgId, default)).ReturnsAsync(teams);

        var svc = CreateService();
        var result = await svc.ListAsync(orgId);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingTeam_ReturnsTeamDto()
    {
        var team = MakeTeam();
        _teamRepo.Setup(r => r.GetByIdAsync(team.Id, team.OrganizationId, default)).ReturnsAsync(team);

        var svc = CreateService();
        var result = await svc.GetByIdAsync(team.Id, team.OrganizationId);

        result.Id.Should().Be(team.Id);
        result.Name.Should().Be(team.Name);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingTeam_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _teamRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((Team?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.GetByIdAsync(id, orgId))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateAsync_WithoutManager_CreatesTeam()
    {
        var orgId = Guid.NewGuid();
        var request = new CreateTeamRequest("Dev Team", null);

        var svc = CreateService();
        var result = await svc.CreateAsync(request, orgId, Guid.NewGuid());

        result.Name.Should().Be("Dev Team");
        result.OrganizationId.Should().Be(orgId);
        result.ManagerId.Should().BeNull();
        _teamRepo.Verify(r => r.AddAsync(It.IsAny<Team>(), default), Times.Once);
        _outboxRepo.Verify(r => r.Add(It.Is<IdentityApi.Domain.Entities.OutboxEvent>(e => e.EventType == "team.updated")), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithValidManager_CreatesTeam()
    {
        var orgId = Guid.NewGuid();
        var manager = MakeUser(orgId: orgId);
        _userRepo.Setup(r => r.GetByIdAsync(manager.Id, orgId, default)).ReturnsAsync(manager);

        var request = new CreateTeamRequest("Dev Team", manager.Id);

        var svc = CreateService();
        var result = await svc.CreateAsync(request, orgId, Guid.NewGuid());

        result.ManagerId.Should().Be(manager.Id);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidManager_ThrowsNotFoundException()
    {
        var orgId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        _userRepo.Setup(r => r.GetByIdAsync(managerId, orgId, default)).ReturnsAsync((User?)null);

        var request = new CreateTeamRequest("Dev Team", managerId);

        var svc = CreateService();
        await svc.Invoking(s => s.CreateAsync(request, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateAsync_ExistingTeam_UpdatesFields()
    {
        var orgId = Guid.NewGuid();
        var team = MakeTeam(orgId: orgId);
        var manager = MakeUser(orgId: orgId);

        _teamRepo.Setup(r => r.GetByIdAsync(team.Id, orgId, default)).ReturnsAsync(team);
        _userRepo.Setup(r => r.GetByIdAsync(manager.Id, orgId, default)).ReturnsAsync(manager);

        var svc = CreateService();
        var request = new UpdateTeamRequest("Renamed Team", manager.Id);
        var result = await svc.UpdateAsync(team.Id, request, orgId, Guid.NewGuid());

        result.Name.Should().Be("Renamed Team");
        result.ManagerId.Should().Be(manager.Id);
        _teamRepo.Verify(r => r.Update(team), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingTeam_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _teamRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((Team?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.UpdateAsync(id, new UpdateTeamRequest(null, null), orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteAsync_ExistingTeam_DeletesTeam()
    {
        var team = MakeTeam();
        _teamRepo.Setup(r => r.GetByIdAsync(team.Id, team.OrganizationId, default)).ReturnsAsync(team);

        var svc = CreateService();
        await svc.DeleteAsync(team.Id, team.OrganizationId, Guid.NewGuid());

        _teamRepo.Verify(r => r.Delete(team), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingTeam_ThrowsNotFoundException()
    {
        var id = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        _teamRepo.Setup(r => r.GetByIdAsync(id, orgId, default)).ReturnsAsync((Team?)null);

        var svc = CreateService();
        await svc.Invoking(s => s.DeleteAsync(id, orgId, Guid.NewGuid()))
            .Should().ThrowAsync<NotFoundException>();
    }
}
