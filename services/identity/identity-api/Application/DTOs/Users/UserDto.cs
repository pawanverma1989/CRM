namespace IdentityApi.Application.DTOs.Users;
public record UserDto(Guid Id, Guid OrganizationId, Guid? TeamId, string Email, string FirstName, string? LastName, string? Phone, string Role, string Status, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
