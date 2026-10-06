namespace IdentityApi.Application.DTOs.Users;
using IdentityApi.Domain.Entities;

/// <summary>Single place that maps a <see cref="User"/> entity to its API DTO (used by every service).</summary>
public static class UserMappings
{
    public static UserDto ToDto(this User u) => new(
        u.Id, u.OrganizationId, u.TeamId, u.Email, u.FirstName, u.LastName, u.Phone, u.Role, u.Status,
        u.LastLoginAt, u.CreatedAt, u.UpdatedAt, u.MustChangePassword);
}
