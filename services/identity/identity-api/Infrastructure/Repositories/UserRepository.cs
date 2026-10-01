namespace IdentityApi.Infrastructure.Repositories;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
        => await context.Users
            .Where(u => u.Id == id && u.OrganizationId == organizationId && u.IsActive)
            .FirstOrDefaultAsync(ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<IReadOnlyList<User>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default)
        => await context.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && u.IsActive)
            .OrderBy(u => u.FirstName)
            .ToListAsync(ct);

    public async Task<Guid[]?> GetVisibleOwnerIdsAsync(Guid userId, CancellationToken ct = default)
        => await context.UserVisibilities
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .Select(v => v.VisibleOwnerIds)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await context.Users.AddAsync(user, ct);

    public void Update(User user)
        => context.Users.Update(user);
}
