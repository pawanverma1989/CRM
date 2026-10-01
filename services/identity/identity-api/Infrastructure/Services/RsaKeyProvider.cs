namespace IdentityApi.Infrastructure.Services;
using System.Security.Cryptography;
using IdentityApi.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface IRsaKeyProvider
{
    RSA GetPrivateKey();
    RSA GetPublicKey();
}

public sealed class RsaKeyProvider : IRsaKeyProvider, IDisposable
{
    private readonly RSA _rsa;

    public RsaKeyProvider(IOptions<JwtSettings> settings, ILogger<RsaKeyProvider> logger, IHostEnvironment env)
    {
        _rsa = RSA.Create(2048);
        var s = settings.Value;

        if (!string.IsNullOrWhiteSpace(s.PrivateKeyPem))
        {
            _rsa.ImportFromPem(s.PrivateKeyPem.Replace("\\n", "\n").AsSpan());
            logger.LogInformation("JWT RSA key loaded from configuration.");
            return;
        }

        if (File.Exists(s.PrivateKeyPath))
        {
            _rsa.ImportFromPem(File.ReadAllText(s.PrivateKeyPath).AsSpan());
            logger.LogInformation("JWT RSA key loaded from file {Path}.", s.PrivateKeyPath);
            return;
        }

        if (!env.IsDevelopment())
            throw new InvalidOperationException("JWT RSA private key is required in production. Set Jwt:PrivateKeyPem or Jwt:PrivateKeyPath.");

        logger.LogWarning("No RSA key configured. Generated ephemeral key — JWT sessions will not survive restart. Set Jwt:PrivateKeyPem for stable sessions.");
    }

    public RSA GetPrivateKey() => _rsa;

    public RSA GetPublicKey()
    {
        var pub = RSA.Create();
        pub.ImportRSAPublicKey(_rsa.ExportRSAPublicKey(), out _);
        return pub;
    }

    public void Dispose() => _rsa.Dispose();
}
