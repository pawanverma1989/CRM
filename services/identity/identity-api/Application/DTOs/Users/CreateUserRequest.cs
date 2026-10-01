namespace IdentityApi.Application.DTOs.Users;
public record CreateUserRequest(string Email, string Password, string FirstName, string? LastName, string? Phone, string Role, Guid? TeamId);
