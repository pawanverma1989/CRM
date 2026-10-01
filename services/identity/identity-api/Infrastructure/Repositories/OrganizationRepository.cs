namespace IdentityApi.Infrastructure.Repositories;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class OrganizationRepository(IdentityDbContext context) : IOrganizationRepository
{
    public async Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Organizations.FindAsync([id], ct);

    public async Task AddAsync(Organization org, CancellationToken ct = default)
        => await context.Organizations.AddAsync(org, ct);

    public void Update(Organization org)
        => context.Organizations.Update(org);
}
