namespace LeadApi.Settings;

/// <summary>
/// HTTP client settings for cross-service calls (conversion saga).
/// Bound from the <c>ServiceClients</c> section of appsettings.
/// </summary>
public class ServiceClientSettings
{
    /// <summary>Base URL for the Customer service API.</summary>
    public string CustomerApiBaseUrl { get; set; } = "http://customer-api:8080";

    /// <summary>Base URL for the Sales service API.</summary>
    public string SalesApiBaseUrl { get; set; } = "http://sales-api:8080";

    /// <summary>Service-to-service bearer token (issued by identity service for this service).</summary>
    public string ServiceToken { get; set; } = string.Empty;

    /// <summary>HTTP timeout for cross-service calls in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}
