namespace IdentityApi.Domain.Interfaces;
using IdentityApi.Domain.Entities;

public interface ITeamRepository
{
    Task<Team?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<Team>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task AddAsync(Team team, CancellationToken ct = default);
    void Update(Team team);
    void Delete(Team team);
}
