namespace CustomerApi.Settings;

/// <summary>Token validation parameters. Bound from the <c>Jwt</c> configuration section.</summary>
public class JwtSettings
{
    public string Issuer { get; set; } = "crm-identity";
    public string Audience { get; set; } = "crm-services";
}

/// <summary>
/// This service validates tokens only; it never issues them. Signing keys come from the identity
/// service's JWKS endpoint (docs/architecture.md §6: one of the few permitted synchronous calls).
/// Bound from the <c>Identity</c> configuration section.
/// </summary>
public class IdentitySettings
{
    public string JwksUrl { get; set; } = "http://identity-api:8080/.well-known/jwks.json";

    /// <summary>How long a fetched key set is cached before it is refreshed (~1 h per §6).</summary>
    public int JwksCacheMinutes { get; set; } = 60;

    public int JwksTimeoutSeconds { get; set; } = 5;
    public int JwksRetryCount { get; set; } = 3;
}
