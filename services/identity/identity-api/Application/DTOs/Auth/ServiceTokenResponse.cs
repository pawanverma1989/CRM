namespace IdentityApi.Application.DTOs.Auth;
public record ServiceTokenResponse(string AccessToken, int ExpiresIn, string TokenType);
