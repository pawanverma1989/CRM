namespace IdentityApi.Application.Interfaces;
using IdentityApi.Application.DTOs.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> ListAsync(Guid organizationId, CancellationToken ct = default);
    Task<UserDto> GetByIdAsync(Guid id, Guid organizationId, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(Guid id, UpdateUserRequest request, Guid organizationId, Guid actorId, CancellationToken ct = default);
    Task DeactivateAsync(Guid id, Guid organizationId, Guid actorId, CancellationToken ct = default);
}
