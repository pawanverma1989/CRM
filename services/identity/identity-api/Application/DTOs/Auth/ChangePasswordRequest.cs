namespace IdentityApi.Application.DTOs.Auth;
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
