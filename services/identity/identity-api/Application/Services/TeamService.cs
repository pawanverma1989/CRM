namespace IdentityApi.Application.Services;
using System.Text.Json;
using IdentityApi.Application.DTOs.Teams;
using IdentityApi.Application.Exceptions;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;

public class TeamService(
    ITeamRepository teamRepository,
    IUserRepository userRepository,
    IOutboxRepository outboxRepository,
    IdentityDbContext context) : ITeamService
{
    public async Task<IReadOnlyList<TeamDto>> ListAsync(Guid organizationId, CancellationToken ct = default)
    {
        var teams = await teamRepository.ListByOrganizationAsync(organizationId, ct);
        return teams.Select(ToDto).ToList();
    }

    public async Task<TeamDto> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
    {
        var team = await teamRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"Team {id} not found.");
        return ToDto(team);
    }

    public async Task<TeamDto> CreateAsync(CreateTeamRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        if (request.ManagerId.HasValue)
        {
            _ = await userRepository.GetByIdAsync(request.ManagerId.Value, organizationId, ct)
                ?? throw new NotFoundException($"Manager {request.ManagerId} not found in this organization.");
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            ManagerId = request.ManagerId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await teamRepository.AddAsync(team, ct);
        outboxRepository.Add(BuildTeamEvent(team, "team.updated", actorId));
        await context.SaveChangesAsync(ct);
        return ToDto(team);
    }

    public async Task<TeamDto> UpdateAsync(Guid id, UpdateTeamRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var team = await teamRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"Team {id} not found.");

        if (request.ManagerId.HasValue)
        {
            _ = await userRepository.GetByIdAsync(request.ManagerId.Value, organizationId, ct)
                ?? throw new NotFoundException($"Manager {request.ManagerId} not found in this organization.");
        }

        if (request.Name is not null) team.Name = request.Name;
        if (request.ManagerId.HasValue) team.ManagerId = request.ManagerId;
        team.UpdatedAt = DateTimeOffset.UtcNow;

        teamRepository.Update(team);
        outboxRepository.Add(BuildTeamEvent(team, "team.updated", actorId));
        await context.SaveChangesAsync(ct);
        return ToDto(team);
    }

    public async Task DeleteAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default)
    {
        var team = await teamRepository.GetByIdAsync(id, organizationId, ct)
            ?? throw new NotFoundException($"Team {id} not found.");
        teamRepository.Delete(team);
        await context.SaveChangesAsync(ct);
    }

    private OutboxEvent BuildTeamEvent(Team team, string eventType, Guid actorId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = team.OrganizationId,
        AggregateType = "team",
        AggregateId = team.Id,
        EventType = eventType,
        Payload = JsonSerializer.Serialize(new { team_id = team.Id, name = team.Name, manager_id = team.ManagerId }),
        OccurredAt = DateTimeOffset.UtcNow
    };

    private static TeamDto ToDto(Team t) => new(t.Id, t.OrganizationId, t.Name, t.ManagerId, t.CreatedAt, t.UpdatedAt);
}
