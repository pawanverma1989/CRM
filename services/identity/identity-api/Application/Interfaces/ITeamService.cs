namespace IdentityApi.Application.Interfaces;
using IdentityApi.Application.DTOs.Teams;

public interface ITeamService
{
    Task<IReadOnlyList<TeamDto>> ListAsync(Guid organizationId, CancellationToken ct = default);
    Task<TeamDto> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<TeamDto> CreateAsync(CreateTeamRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task<TeamDto> UpdateAsync(Guid id, UpdateTeamRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default);
}
