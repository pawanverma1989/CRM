namespace LeadApi.Application.DTOs;
using System.Text.Json;

public sealed record WebFormDto(
    Guid Id,
    string Name,
    string PublicKey,
    JsonElement Fields,
    JsonElement RequiredFields,
    Guid? LeadSourceId,
    Guid? DefaultOwnerId,
    string? ConsentText,
    string? SuccessMessage,
    string? RedirectUrl,
    bool CaptchaEnabled,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Embed snippet returned by GET /web-forms/{id}/embed.</summary>
public sealed record WebFormEmbedDto(
    Guid Id,
    string PublicKey,
    string SubmitUrl,
    string EmbedSnippet);

public sealed record CreateWebFormRequest
{
    public string Name { get; init; } = string.Empty;
    public JsonElement? Fields { get; init; }
    public JsonElement? RequiredFields { get; init; }
    public Guid? LeadSourceId { get; init; }
    public Guid? DefaultOwnerId { get; init; }
    public string? ConsentText { get; init; }
    public string? SuccessMessage { get; init; }
    public string? RedirectUrl { get; init; }
    public bool CaptchaEnabled { get; init; } = true;
}

public sealed record UpdateWebFormRequest
{
    public string? Name { get; init; }
    public JsonElement? Fields { get; init; }
    public JsonElement? RequiredFields { get; init; }
    public Guid? LeadSourceId { get; init; }
    public Guid? DefaultOwnerId { get; init; }
    public string? ConsentText { get; init; }
    public string? SuccessMessage { get; init; }
    public string? RedirectUrl { get; init; }
    public bool? CaptchaEnabled { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>Body for the public form submission endpoint (WEB-4, body size ≤ 20KB).</summary>
public sealed record PublicFormSubmitRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? CompanyName { get; init; }
    public string? JobTitle { get; init; }
    public string? Notes { get; init; }
    public bool ConsentGiven { get; init; }
    public JsonElement? CustomFields { get; init; }
    public string? UtmSource { get; init; }
    public string? UtmMedium { get; init; }
    public string? UtmCampaign { get; init; }
}

public sealed record PublicFormSubmitResponse(
    Guid LeadId,
    string? SuccessMessage,
    string? RedirectUrl);
