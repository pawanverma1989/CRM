namespace LeadApi.Settings;

/// <summary>
/// Identity service integration settings. Bound from the <c>Identity</c> section of appsettings.
/// </summary>
public class IdentitySettings
{
    /// <summary>URL for the identity service's JWKS endpoint used to validate tokens.</summary>
    public string JwksUrl { get; set; } = "http://identity-api:8080/.well-known/jwks.json";

    /// <summary>How long (minutes) to cache the signing keys before refreshing.</summary>
    public int JwksCacheMinutes { get; set; } = 60;

    /// <summary>HTTP timeout in seconds for the JWKS fetch.</summary>
    public int JwksTimeoutSeconds { get; set; } = 5;

    /// <summary>Number of retries for JWKS fetch before falling back to cached keys.</summary>
    public int JwksRetryCount { get; set; } = 3;
}
