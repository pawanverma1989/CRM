namespace LeadApi.Infrastructure.Services;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

/// <summary>
/// Payload sent to Customer service when creating a company during conversion.
/// </summary>
public record CreateCompanyPayload(
    string Name,
    string? Domain,
    string? Phone,
    Guid SourceLeadId);

/// <summary>
/// Payload sent to Customer service when creating a contact during conversion.
/// </summary>
public record CreateContactPayload(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    Guid? CompanyId,
    Guid SourceLeadId);

/// <summary>Minimal response shape we read from Customer service.</summary>
public record CustomerRecordResponse(Guid Id, string? Name);

/// <summary>
/// HTTP client for the Customer service API (lead conversion saga — CLAUDE.md rule 5).
/// Sends the service Bearer token and an idempotency key (the lead_id) so a retried saga
/// step does not create duplicate companies or contacts.
/// </summary>
public interface ICustomerApiClient
{
    Task<Guid?> CreateCompanyAsync(CreateCompanyPayload payload, Guid leadId, Guid? actorUserId, CancellationToken ct);
    Task<Guid?> CreateContactAsync(CreateContactPayload payload, Guid leadId, Guid? actorUserId, CancellationToken ct);
}

public class CustomerApiClient : ICustomerApiClient
{
    private readonly HttpClient _http;
    private readonly ServiceClientSettings _settings;
    private readonly ILogger<CustomerApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CustomerApiClient(
        HttpClient http,
        IOptions<ServiceClientSettings> settings,
        ILogger<CustomerApiClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _http.BaseAddress = new Uri(_settings.CustomerApiBaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_settings.ServiceToken))
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ServiceToken);
    }

    public async Task<Guid?> CreateCompanyAsync(CreateCompanyPayload payload, Guid leadId, Guid? actorUserId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/customer/v1/companies");
        request.Headers.Add("Idempotency-Key", $"lead-conversion-company-{leadId}");

        if (actorUserId.HasValue)
            request.Headers.Add("X-Acting-User-Id", actorUserId.Value.ToString());

        request.Content = JsonContent.Create(payload, options: JsonOptions);

        return await SendAsync(request, "company", ct);
    }

    public async Task<Guid?> CreateContactAsync(CreateContactPayload payload, Guid leadId, Guid? actorUserId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/customer/v1/contacts");
        request.Headers.Add("Idempotency-Key", $"lead-conversion-contact-{leadId}");

        if (actorUserId.HasValue)
            request.Headers.Add("X-Acting-User-Id", actorUserId.Value.ToString());

        request.Content = JsonContent.Create(payload, options: JsonOptions);

        return await SendAsync(request, "contact", ct);
    }

    private async Task<Guid?> SendAsync(HttpRequestMessage request, string entityType, CancellationToken ct)
    {
        try
        {
            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // 409 means the record already exists — treat as success and extract the id.
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (body.TryGetProperty("conflictingRecord", out var conflict)
                    && conflict.TryGetProperty("id", out var idEl)
                    && Guid.TryParse(idEl.GetString(), out var existingId))
                {
                    _logger.LogInformation("Customer service returned 409 for {EntityType}; using existing id {Id}.", entityType, existingId);
                    return existingId;
                }

                _logger.LogWarning("Customer service returned 409 for {EntityType} but no conflicting id in body.", entityType);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<CustomerRecordResponse>(
                options: JsonOptions, cancellationToken: ct);

            return result?.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to create {EntityType} in Customer service.", entityType);
            throw;
        }
    }
}
