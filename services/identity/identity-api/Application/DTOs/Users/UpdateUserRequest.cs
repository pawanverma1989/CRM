namespace IdentityApi.Application.DTOs.Users;
public record UpdateUserRequest(string? FirstName, string? LastName, string? Phone, string? Role, Guid? TeamId, bool? IsActive);
