namespace LeadApi.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Retry;

/// <summary>
/// Resolves the identity service's RS256 signing keys from its JWKS endpoint.
/// Keys are cached for ~1 h; if identity is unreachable the last known-good set is used.
/// </summary>
public interface IJwksProvider
{
    IEnumerable<SecurityKey> GetSigningKeys(string? kid);
}

public sealed class JwksProvider : IJwksProvider
{
    private const string CacheKey = "identity-jwks";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly IdentitySettings _settings;
    private readonly ILogger<JwksProvider> _logger;
    private readonly ResiliencePipeline _retry;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyCollection<SecurityKey> _lastKnownGood = [];

    public JwksProvider(
        HttpClient http,
        IMemoryCache cache,
        IOptions<IdentitySettings> settings,
        ILogger<JwksProvider> logger)
    {
        _settings = settings.Value;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(_settings.JwksTimeoutSeconds);
        _cache = cache;
        _logger = logger;

        _retry = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _settings.JwksRetryCount,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<Exception>()
            })
            .Build();
    }

    public IEnumerable<SecurityKey> GetSigningKeys(string? kid)
    {
        var keys = _cache.Get<IReadOnlyCollection<SecurityKey>>(CacheKey) ?? Load();

        if (string.IsNullOrEmpty(kid)) return keys;

        var matching = keys.Where(k => k.KeyId == kid).ToArray();
        return matching.Length > 0 ? matching : keys;
    }

    private IReadOnlyCollection<SecurityKey> Load()
    {
        _refreshLock.Wait();
        try
        {
            var cached = _cache.Get<IReadOnlyCollection<SecurityKey>>(CacheKey);
            if (cached is not null) return cached;

            try
            {
                var keys = _retry.Execute(() => FetchAsync().GetAwaiter().GetResult());
                if (keys.Count > 0)
                {
                    _lastKnownGood = keys;
                    _cache.Set(CacheKey, keys, TimeSpan.FromMinutes(_settings.JwksCacheMinutes));
                    _logger.LogInformation("Loaded {Count} signing key(s) from the identity JWKS endpoint.", keys.Count);
                    return keys;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not reach the identity JWKS endpoint; falling back to {Count} cached key(s).",
                    _lastKnownGood.Count);
            }

            return _lastKnownGood;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<IReadOnlyCollection<SecurityKey>> FetchAsync()
    {
        var json = await _http.GetStringAsync(_settings.JwksUrl);
        var jwks = new JsonWebKeySet(json);
        return [.. jwks.GetSigningKeys()];
    }
}
