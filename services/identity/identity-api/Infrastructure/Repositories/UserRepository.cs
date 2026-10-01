namespace IdentityApi.Infrastructure.Repositories;
using IdentityApi.Domain.Entities;
using IdentityApi.Domain.Interfaces;
using IdentityApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

public class UserRepository(IdentityDbContext context) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default)
        => await context.Users
            .Where(u => u.Id == id && u.OrganizationId == organizationId && u.Status != "deactivated")
            .FirstOrDefaultAsync(ct);

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public async Task<IReadOnlyList<User>> ListByOrganizationAsync(Guid organizationId, string? role, Guid? teamId, string? status, string? search, CancellationToken ct = default)
    {
        var query = context.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organizationId);

        if (role is not null)
            query = query.Where(u => u.Role == role);

        if (teamId.HasValue)
            query = query.Where(u => u.TeamId == teamId.Value);

        if (status is not null)
            query = query.Where(u => u.Status == status);

        if (search is not null)
            query = query.Where(u => u.Email.Contains(search) || u.FirstName.Contains(search) || (u.LastName != null && u.LastName.Contains(search)));

        return await query.OrderBy(u => u.FirstName).ToListAsync(ct);
    }

    public async Task<Guid[]?> GetVisibleOwnerIdsAsync(Guid userId, CancellationToken ct = default)
        => await context.UserVisibilities
            .AsNoTracking()
            .Where(v => v.UserId == userId)
            .Select(v => v.VisibleOwnerIds)
            .FirstOrDefaultAsync(ct);

    public async Task<int> CountActiveAdminsAsync(Guid orgId, CancellationToken ct = default)
        => await context.Users
            .CountAsync(u => u.OrganizationId == orgId && u.Role == "admin" && u.Status == "active", ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
        => await context.Users.AddAsync(user, ct);

    public void Update(User user)
        => context.Users.Update(user);
}
