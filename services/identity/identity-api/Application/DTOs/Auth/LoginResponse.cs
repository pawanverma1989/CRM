namespace IdentityApi.Application.DTOs.Auth;
using IdentityApi.Application.DTOs.Users;
public record LoginResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType, UserDto User);
