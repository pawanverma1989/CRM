namespace IdentityApi.Infrastructure.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using IdentityApi.Application.Interfaces;
using IdentityApi.Domain.Entities;
using IdentityApi.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

public class JwtService(IRsaKeyProvider keyProvider, IOptions<JwtSettings> settings) : ITokenService
{
    private readonly JwtSettings _settings = settings.Value;

    public string GenerateAccessToken(User user, Guid[]? visibleOwnerIds)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("organization_id", user.OrganizationId.ToString()),
            new("role", user.Role),
        };

        if (user.TeamId.HasValue)
            claims.Add(new Claim("team_id", user.TeamId.Value.ToString()));

        if (user.Role != "admin" && visibleOwnerIds is not null)
            claims.Add(new Claim("visible_owner_ids", JsonSerializer.Serialize(visibleOwnerIds)));

        var key = new RsaSecurityKey(keyProvider.GetPrivateKey()) { KeyId = _settings.KeyId };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string GenerateServiceToken(string clientId, string serviceName, Guid organizationId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("service_name", serviceName),
            new("organization_id", organizationId.ToString()),
            new("role", "service"),
        };

        var key = new RsaSecurityKey(keyProvider.GetPrivateKey()) { KeyId = _settings.KeyId };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public JsonWebKeySet GetPublicKeySet()
    {
        using var pubKey = keyProvider.GetPublicKey();
        var secKey = new RsaSecurityKey(pubKey) { KeyId = _settings.KeyId };
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(secKey);
        jwk.Use = "sig";
        jwk.Alg = "RS256";
        return new JsonWebKeySet { Keys = { jwk } };
    }
}
