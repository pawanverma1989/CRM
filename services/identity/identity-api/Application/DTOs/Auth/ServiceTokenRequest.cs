namespace IdentityApi.Application.DTOs.Auth;
public record ServiceTokenRequest(string GrantType, string ClientId, string ClientSecret);
