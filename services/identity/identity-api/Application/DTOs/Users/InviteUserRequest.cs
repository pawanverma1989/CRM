namespace IdentityApi.Application.DTOs.Users;
public record InviteUserRequest(string Email, string Role, Guid? TeamId);
