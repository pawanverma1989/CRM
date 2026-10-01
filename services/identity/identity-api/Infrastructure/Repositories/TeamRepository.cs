namespace IdentityApi.Infrastructure.Repositories;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class TeamRepository(IdentityDbContext context) : ITeamRepository
{
    public async Task<Team?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
        => await context.Teams
            .Where(t => t.Id == id && t.OrganizationId == organizationId)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Team>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await context.Teams
            .AsNoTracking()
            .Where(t => t.OrganizationId == organizationId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task AddAsync(Team team, CancellationToken ct = default)
        => await context.Teams.AddAsync(team, ct);

    public void Update(Team team)
        => context.Teams.Update(team);

    public void Delete(Team team)
        => context.Teams.Remove(team);
}
