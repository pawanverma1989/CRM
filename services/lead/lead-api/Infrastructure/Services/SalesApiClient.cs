namespace LeadApi.Infrastructure.Services;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

/// <summary>
/// Payload sent to Sales service when creating a deal during conversion.
/// </summary>
public record CreateDealPayload(
    string? Name,
    Guid? CompanyId,
    Guid? ContactId,
    Guid SourceLeadId,
    string? Currency);

/// <summary>
/// HTTP client for the Sales service API (lead conversion saga — CLAUDE.md rule 5).
/// </summary>
public interface ISalesApiClient
{
    Task<Guid?> CreateDealAsync(CreateDealPayload payload, Guid leadId, Guid? actorUserId, CancellationToken ct);
}

public class SalesApiClient : ISalesApiClient
{
    private readonly HttpClient _http;
    private readonly ServiceClientSettings _settings;
    private readonly ILogger<SalesApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SalesApiClient(
        HttpClient http,
        IOptions<ServiceClientSettings> settings,
        ILogger<SalesApiClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _http.BaseAddress = new Uri(_settings.SalesApiBaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_settings.ServiceToken))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ServiceToken);
    }

    public async Task<Guid?> CreateDealAsync(CreateDealPayload payload, Guid leadId, Guid? actorUserId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/sales/v1/deals");
        request.Headers.Add("Idempotency-Key", $"lead-conversion-deal-{leadId}");

        if (actorUserId.HasValue)
            request.Headers.Add("X-Acting-User-Id", actorUserId.Value.ToString());

        request.Content = JsonContent.Create(payload, options: JsonOptions);

        try
        {
            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (body.TryGetProperty("conflictingRecord", out var conflict)
                    && conflict.TryGetProperty("id", out var idEl)
                    && Guid.TryParse(idEl.GetString(), out var existingId))
                {
                    _logger.LogInformation("Sales service returned 409 for deal; using existing id {Id}.", existingId);
                    return existingId;
                }

                _logger.LogWarning("Sales service returned 409 for deal but no conflicting id in body.");
                return null;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<CustomerRecordResponse>(
                options: JsonOptions, cancellationToken: ct);

            return result?.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create deal in Sales service.");
            throw;
        }
    }
}
