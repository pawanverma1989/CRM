namespace CustomerApi.Infrastructure.Context;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using CustomerApi.Application.Exceptions;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Everything a query needs to know about the caller. Resolved once per request from the token
/// (and, for service callers, the documented X-Acting-* headers). <see cref="OrganizationId"/> is
/// always the token claim, never a header or a request body (NFR-6).
/// </summary>
public interface IRequestContext
{
    Guid OrganizationId { get; }

    /// <summary>The user whose action this is: the token subject, or the acting user for a service call.</summary>
    Guid ActorUserId { get; }

    /// <summary>admin | manager | sales_rep — for a service call, the acting user's role.</summary>
    string Role { get; }

    /// <summary><c>null</c> means every owner in the organization (admins).</summary>
    Guid[]? VisibleOwnerIds { get; }

    bool IsAdmin { get; }
    bool IsManagerOrAbove { get; }
    bool IsService { get; }

    /// <summary>The calling service's name, for service tokens.</summary>
    string? ServiceName { get; }
}

public sealed class HttpRequestContext : IRequestContext
{
    public const string ActingUserIdHeader = "X-Acting-User-Id";
    public const string ActingUserRoleHeader = "X-Acting-User-Role";
    public const string VisibleOwnerIdsHeader = "X-Visible-Owner-Ids";

    private readonly HttpContext _http;
    private readonly Lazy<Guid> _organizationId;
    private readonly Lazy<Guid> _actorUserId;
    private readonly Lazy<string> _role;
    private readonly Lazy<Guid[]?> _visibleOwnerIds;

    public HttpRequestContext(IHttpContextAccessor accessor)
    {
        _http = accessor.HttpContext
            ?? throw new UnauthorizedException("No active request.");

        _organizationId = new Lazy<Guid>(ResolveOrganizationId);
        _actorUserId = new Lazy<Guid>(ResolveActorUserId);
        _role = new Lazy<string>(ResolveRole);
        _visibleOwnerIds = new Lazy<Guid[]?>(ResolveVisibleOwnerIds);
    }

    private ClaimsPrincipal User => _http.User;

    public Guid OrganizationId => _organizationId.Value;
    public Guid ActorUserId => _actorUserId.Value;
    public string Role => _role.Value;
    public Guid[]? VisibleOwnerIds => _visibleOwnerIds.Value;
    public bool IsAdmin => Role == "admin";
    public bool IsManagerOrAbove => Role is "admin" or "manager";
    public bool IsService => User.FindFirstValue("role") == "service";
    public string? ServiceName => User.FindFirstValue("service_name");

    private Guid ResolveOrganizationId()
    {
        var claim = User.FindFirstValue("organization_id");
        return Guid.TryParse(claim, out var id)
            ? id
            : throw new UnauthorizedException("The token carries no organization_id claim.");
    }

    private Guid ResolveActorUserId()
    {
        if (IsService)
        {
            var header = _http.Request.Headers[ActingUserIdHeader].FirstOrDefault();
            return Guid.TryParse(header, out var acting)
                ? acting
                : throw new CustomerValidationException(ActingUserIdHeader,
                    "A service token must name the acting user in the X-Acting-User-Id header.");
        }

        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(sub, out var id)
            ? id
            : throw new UnauthorizedException("The token carries no usable sub claim.");
    }

    private string ResolveRole()
    {
        if (!IsService) return User.FindFirstValue("role") ?? "sales_rep";

        var header = _http.Request.Headers[ActingUserRoleHeader].FirstOrDefault();
        return string.IsNullOrWhiteSpace(header) ? "sales_rep" : header.Trim().ToLowerInvariant();
    }

    private Guid[]? ResolveVisibleOwnerIds()
    {
        if (IsAdmin) return null;                       // everything in the organization

        if (IsService)
        {
            var header = _http.Request.Headers[VisibleOwnerIdsHeader].FirstOrDefault();
            var fromHeader = ParseCsvGuids(header);
            return fromHeader.Length > 0 ? fromHeader : [ActorUserId];
        }

        var claim = User.FindFirstValue("visible_owner_ids");
        if (string.IsNullOrWhiteSpace(claim)) return [ActorUserId];

        try
        {
            var ids = JsonSerializer.Deserialize<Guid[]>(claim);
            return ids is { Length: > 0 } ? ids : [ActorUserId];
        }
        catch (JsonException)
        {
            return [ActorUserId];
        }
    }

    private static Guid[] ParseCsvGuids(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                     .Where(g => g.HasValue)
                     .Select(g => g!.Value)
                     .Distinct()];
}

/// <summary>A caller fixed at construction time — used by background workers, which have no HTTP request.</summary>
public sealed class StaticRequestContext(
    Guid organizationId,
    Guid actorUserId,
    string role = "admin",
    Guid[]? visibleOwnerIds = null,
    bool isService = true,
    string? serviceName = "customer") : IRequestContext
{
    public Guid OrganizationId { get; } = organizationId;
    public Guid ActorUserId { get; } = actorUserId;
    public string Role { get; } = role;
    public Guid[]? VisibleOwnerIds { get; } = visibleOwnerIds;
    public bool IsAdmin => Role == "admin";
    public bool IsManagerOrAbove => Role is "admin" or "manager";
    public bool IsService { get; } = isService;
    public string? ServiceName { get; } = serviceName;
}
