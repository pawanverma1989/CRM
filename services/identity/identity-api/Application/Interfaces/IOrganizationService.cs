namespace IdentityApi.Application.Interfaces;
using IdentityApi.Application.DTOs.Organizations;
using IdentityApi.Application.DTOs.Users;

public interface IOrganizationService
{
    Task<OrganizationDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(OrganizationDto Org, UserDto AdminUser)> CreateWithAdminAsync(CreateOrganizationRequest request, CancellationToken ct = default);
    Task<OrganizationDto> UpdateAsync(Guid id, UpdateOrganizationRequest request, Guid actorId, CancellationToken ct = default);
}
