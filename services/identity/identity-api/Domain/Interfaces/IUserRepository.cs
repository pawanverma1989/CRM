namespace IdentityApi.Domain.Interfaces;
using IdentityApi.Domain.Entities;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);
    Task<Guid[]?> GetVisibleOwnerIdsAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    void Update(User user);
}
