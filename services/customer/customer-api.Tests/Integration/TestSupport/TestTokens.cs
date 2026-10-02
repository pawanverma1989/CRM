namespace CustomerApi.Tests.Integration.TestSupport;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Issues the tokens the tests sign in with. The real service only ever validates tokens, against
/// identity's JWKS endpoint; here the fixture points token validation at this key instead, so the
/// 401 and 403 paths are the production ones rather than a stub authentication handler.
/// </summary>
public static class TestTokens
{
    public const string Issuer = "crm-identity";
    public const string Audience = "crm-services";

    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string SalesRep = "sales_rep";
    public const string Service = "service";

    private const string Secret = "customer-api-integration-test-signing-key-which-is-long-enough";

    public static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes(Secret)) { KeyId = "customer-test-key" };

    /// <summary>
    /// A user token. <paramref name="visibleOwnerIds"/> is omitted for admins, which means
    /// "everything in the organization" (architecture §8).
    /// </summary>
    public static string ForUser(Guid userId, Guid organizationId, string role, Guid[]? visibleOwnerIds = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("organization_id", organizationId.ToString()),
            new("role", role)
        };

        if (role != Admin)
            claims.Add(new Claim(
                "visible_owner_ids",
                JsonSerializer.Serialize(visibleOwnerIds ?? [userId])));

        return Write(claims);
    }

    /// <summary>A service token; the acting user travels in the documented X-Acting-* headers.</summary>
    public static string ForService(Guid organizationId, string serviceName)
        => Write(
        [
            new Claim(JwtRegisteredClaimNames.Sub, serviceName),
            new Claim("organization_id", organizationId.ToString()),
            new Claim("role", Service),
            new Claim("service_name", serviceName)
        ]);

    /// <summary>A token signed with the wrong key, to prove the signature is really checked.</summary>
    public static string WithWrongSignature(Guid userId, Guid organizationId, string role)
    {
        var other = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("a-completely-different-key-that-is-also-long-enough-xx"));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim("organization_id", organizationId.ToString()),
                new Claim("role", role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(other, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Write(IEnumerable<Claim> claims)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
