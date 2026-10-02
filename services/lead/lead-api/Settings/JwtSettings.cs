namespace LeadApi.Settings;

/// <summary>JWT validation parameters. Bound from the <c>Jwt</c> section of appsettings.</summary>
public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
