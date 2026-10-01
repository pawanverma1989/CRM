namespace IdentityApi.Domain.Interfaces;
using IdentityApi.Domain.Entities;

public interface IOrganizationRepository
{
    Task<Organization?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Organization org, CancellationToken ct = default);
    void Update(Organization org);
}
