namespace CustomerApi.Tests.Unit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using CustomerApi.Application.Exceptions;
using CustomerApi.Infrastructure.Context;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

/// <summary>
/// organization_id always comes from the token, never a header or body (NFR-6).
/// Service callers supply the acting user through the documented X-Acting-* headers.
/// </summary>
public class RequestContextTests
{
    private static HttpRequestContext Build(IEnumerable<Claim> claims, Action<IHeaderDictionary>? headers = null)
    {
        var ctx = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        headers?.Invoke(ctx.Request.Headers);
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return new HttpRequestContext(accessor);
    }

    private static Claim[] UserClaims(Guid user, Guid org, string role, Guid[]? visible = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.ToString()),
            new("organization_id", org.ToString()),
            new("role", role)
        };
        if (visible is not null)
            claims.Add(new Claim("visible_owner_ids", JsonSerializer.Serialize(visible)));
        return [.. claims];
    }

    [Fact]
    public void AdminToken_HasNoVisibilityRestriction()
    {
        var org = Guid.NewGuid();
        var ctx = Build(UserClaims(Guid.NewGuid(), org, "admin"));

        ctx.IsAdmin.Should().BeTrue();
        ctx.IsService.Should().BeFalse();
        ctx.VisibleOwnerIds.Should().BeNull("a null set means everything in the organization");
        ctx.OrganizationId.Should().Be(org);
    }

    [Fact]
    public void ManagerToken_ParsesVisibleOwnerIdsFromJsonArrayClaim()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var ctx = Build(UserClaims(a, Guid.NewGuid(), "manager", [a, b]));

        ctx.IsAdmin.Should().BeFalse();
        ctx.VisibleOwnerIds.Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public void SalesRepTokenWithoutVisibilityClaim_SeesOnlyItsOwnRecords()
    {
        var me = Guid.NewGuid();
        var ctx = Build(UserClaims(me, Guid.NewGuid(), "sales_rep"));

        ctx.VisibleOwnerIds.Should().BeEquivalentTo([me]);
    }

    [Fact]
    public void ServiceToken_UsesActingUserHeaderAsActor()
    {
        var org = Guid.NewGuid();
        var acting = Guid.NewGuid();
        var ctx = Build(
            [new Claim(JwtRegisteredClaimNames.Sub, "data-transfer"),
             new Claim("organization_id", org.ToString()),
             new Claim("service_name", "data-transfer"),
             new Claim("role", "service")],
            h =>
            {
                h["X-Acting-User-Id"] = acting.ToString();
                h["X-Acting-User-Role"] = "admin";
            });

        ctx.IsService.Should().BeTrue();
        ctx.IsAdmin.Should().BeTrue();
        ctx.ActorUserId.Should().Be(acting);
        ctx.OrganizationId.Should().Be(org);
        ctx.VisibleOwnerIds.Should().BeNull();
    }

    [Fact]
    public void ServiceToken_NonAdminActingUserWithoutVisibleIds_FallsBackToOwnRecords()
    {
        var acting = Guid.NewGuid();
        var ctx = Build(
            [new Claim(JwtRegisteredClaimNames.Sub, "data-transfer"),
             new Claim("organization_id", Guid.NewGuid().ToString()),
             new Claim("role", "service")],
            h => h["X-Acting-User-Id"] = acting.ToString());

        ctx.VisibleOwnerIds.Should().BeEquivalentTo([acting]);
    }

    [Fact]
    public void ServiceToken_WithExplicitVisibleOwnerIdsHeader_UsesThem()
    {
        var acting = Guid.NewGuid();
        var other = Guid.NewGuid();
        var ctx = Build(
            [new Claim(JwtRegisteredClaimNames.Sub, "data-transfer"),
             new Claim("organization_id", Guid.NewGuid().ToString()),
             new Claim("role", "service")],
            h =>
            {
                h["X-Acting-User-Id"] = acting.ToString();
                h["X-Acting-User-Role"] = "manager";
                h["X-Visible-Owner-Ids"] = $"{acting},{other}";
            });

        ctx.VisibleOwnerIds.Should().BeEquivalentTo([acting, other]);
    }

    [Fact]
    public void ServiceToken_WithoutActingUserHeader_Throws()
    {
        var ctx = Build(
            [new Claim(JwtRegisteredClaimNames.Sub, "data-transfer"),
             new Claim("organization_id", Guid.NewGuid().ToString()),
             new Claim("role", "service")]);

        var act = () => ctx.ActorUserId;
        act.Should().Throw<CustomerValidationException>().WithMessage("*X-Acting-User-Id*");
    }

    [Fact]
    public void OrganizationIdHeader_IsIgnored_TokenClaimWins()
    {
        var org = Guid.NewGuid();
        var ctx = Build(UserClaims(Guid.NewGuid(), org, "admin"),
            h => h["organization_id"] = Guid.NewGuid().ToString());

        ctx.OrganizationId.Should().Be(org);
    }
}
