namespace IdentityApi.Settings;

public class JwtSettings
{
    public string Issuer { get; set; } = "crm-identity";
    public string Audience { get; set; } = "crm-services";
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
    public string KeyId { get; set; } = "crm-identity-key-v1";
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = "jwt-keys/private.pem";
}
