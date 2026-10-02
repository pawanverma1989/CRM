namespace LeadApi.Extensions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    public static Guid GetOrganizationId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue("organization_id")!);

    public static string GetRole(this ClaimsPrincipal user)
        => user.FindFirstValue("role")!;

    public static bool IsService(this ClaimsPrincipal user)
        => user.FindFirstValue("role") == "service";
}
