namespace IdentityApi.Tests.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using IdentityApi.Domain.Entities;
using IdentityApi.Infrastructure.Services;
using IdentityApi.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;

public class JwtServiceTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly JwtSettings _settings = new()
    {
        Issuer = "crm-identity",
        Audience = "crm-services",
        KeyId = "test-key-v1",
        AccessTokenExpiryMinutes = 15,
        RefreshTokenExpiryDays = 7
    };

    private JwtService CreateService()
    {
        var keyProvider = new Mock<IRsaKeyProvider>();
        keyProvider.Setup(k => k.GetPrivateKey()).Returns(_rsa);
        keyProvider.Setup(k => k.GetPublicKey()).Returns(() =>
        {
            var pub = RSA.Create();
            pub.ImportRSAPublicKey(_rsa.ExportRSAPublicKey(), out _);
            return pub;
        });
        return new JwtService(keyProvider.Object, Options.Create(_settings));
    }

    private static User MakeUser(string role = "sales_rep", Guid? teamId = null) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        Email = "user@example.com",
        PasswordHash = "hash",
        FirstName = "Test",
        Role = role,
        TeamId = teamId,
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public void GenerateAccessToken_ContainsRequiredClaims()
    {
        var user = MakeUser();
        var svc = CreateService();

        var token = svc.GenerateAccessToken(user, null);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Subject.Should().Be(user.Id.ToString());
        jwt.Claims.First(c => c.Type == "organization_id").Value.Should().Be(user.OrganizationId.ToString());
        jwt.Claims.First(c => c.Type == "role").Value.Should().Be("sales_rep");
        jwt.Issuer.Should().Be(_settings.Issuer);
    }

    [Fact]
    public void GenerateAccessToken_UserWithTeam_IncludesTeamIdClaim()
    {
        var teamId = Guid.NewGuid();
        var user = MakeUser(teamId: teamId);
        var svc = CreateService();

        var token = svc.GenerateAccessToken(user, null);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "team_id" && c.Value == teamId.ToString());
    }

    [Fact]
    public void GenerateAccessToken_NonAdminWithVisibleOwners_IncludesVisibleOwnerIdsClaim()
    {
        var user = MakeUser(role: "sales_rep");
        var ownerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var svc = CreateService();

        var token = svc.GenerateAccessToken(user, ownerIds);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == "visible_owner_ids");
    }

    [Fact]
    public void GenerateAccessToken_AdminUser_DoesNotIncludeVisibleOwnerIdsClaim()
    {
        var user = MakeUser(role: "admin");
        var ownerIds = new[] { Guid.NewGuid() };
        var svc = CreateService();

        var token = svc.GenerateAccessToken(user, ownerIds);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "visible_owner_ids");
    }

    [Fact]
    public void GenerateAccessToken_TokenIsValidSignature()
    {
        var user = MakeUser();
        var svc = CreateService();

        var tokenString = svc.GenerateAccessToken(user, null);

        var handler = new JwtSecurityTokenHandler();
        using var pubKey = RSA.Create();
        pubKey.ImportRSAPublicKey(_rsa.ExportRSAPublicKey(), out _);

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(pubKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var act = () => handler.ValidateToken(tokenString, validationParams, out _);
        act.Should().NotThrow();
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsDifferentTokensEachCall()
    {
        var svc = CreateService();

        var t1 = svc.GenerateRefreshToken();
        var t2 = svc.GenerateRefreshToken();

        t1.Should().NotBe(t2);
    }

    [Fact]
    public void GenerateRefreshToken_IsBase64String()
    {
        var svc = CreateService();

        var token = svc.GenerateRefreshToken();

        var act = () => Convert.FromBase64String(token);
        act.Should().NotThrow();
        Convert.FromBase64String(token).Should().HaveCount(64);
    }

    [Fact]
    public void GetPublicKeySet_ReturnsJwksWithCorrectKeyId()
    {
        var svc = CreateService();

        var jwks = svc.GetPublicKeySet();

        jwks.Keys.Should().HaveCount(1);
        jwks.Keys[0].KeyId.Should().Be(_settings.KeyId);
        jwks.Keys[0].Use.Should().Be("sig");
        jwks.Keys[0].Alg.Should().Be("RS256");
    }

    public void Dispose() => _rsa.Dispose();
}
