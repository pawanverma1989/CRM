namespace CustomerApi.Application.Mapping;
using System.Text.Json;
using CustomerApi.Application.DTOs;
using CustomerApi.Domain.Entities;

/// <summary>
/// Names resolved once per request from the local copies (<c>user_refs</c>, <c>picklists</c>) so a
/// list never calls another service to render an owner or a picklist label (CLAUDE.md rule 1).
/// </summary>
public sealed class LookupNames
{
    public static readonly LookupNames Empty = new();

    public IReadOnlyDictionary<Guid, string> Owners { get; init; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, string> PicklistValues { get; init; } = new Dictionary<Guid, string>();
    public IReadOnlyDictionary<Guid, string> CompanyNames { get; init; } = new Dictionary<Guid, string>();

    public string? Owner(Guid? id) => id is not null && Owners.TryGetValue(id.Value, out var v) ? v : null;
    public string? Picklist(Guid? id) => id is not null && PicklistValues.TryGetValue(id.Value, out var v) ? v : null;
    public string? Company(Guid? id) => id is not null && CompanyNames.TryGetValue(id.Value, out var v) ? v : null;
}

/// <summary>
/// Entity to DTO. The same DTO is the <c>*.created</c> / <c>*.updated</c> event payload, so
/// consumers never have to call back (§6).
/// </summary>
public static class CustomerMapper
{
    public static readonly JsonElement EmptyObject = JsonSerializer.Deserialize<JsonElement>("{}");

    public static JsonElement Json(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return EmptyObject;
        try { return JsonSerializer.Deserialize<JsonElement>(raw); }
        catch (JsonException) { return EmptyObject; }
    }

    public static CompanyDto ToDto(Company c, LookupNames names) => new(
        c.Id,
        c.OrganizationId,
        c.OwnerId,
        names.Owner(c.OwnerId),
        c.Name,
        c.Domain,
        c.IndustryId,
        names.Picklist(c.IndustryId),
        c.EmployeeCount,
        c.AnnualRevenue,
        "INR",
        c.Phone,
        c.Website,
        c.AddressLine1,
        c.AddressLine2,
        c.City,
        c.State,
        c.PostalCode,
        c.Country,
        c.Gstin,
        c.Tags,
        Json(c.CustomFields),
        c.MergedIntoId,
        c.Version,
        c.CreatedBy,
        c.CreatedAt,
        c.UpdatedAt,
        c.DeletedAt);

    public static ContactDto ToDto(Contact c, LookupNames names) => new(
        c.Id,
        c.OrganizationId,
        c.CompanyId,
        names.Company(c.CompanyId),
        c.OwnerId,
        names.Owner(c.OwnerId),
        c.FirstName,
        c.LastName,
        c.Email,
        c.Phone,
        c.PhoneNormalized,
        c.Mobile,
        c.JobTitle,
        c.AddressLine1,
        c.AddressLine2,
        c.City,
        c.State,
        c.PostalCode,
        c.Country,
        c.SourceId,
        names.Picklist(c.SourceId),
        c.SourceLeadId,
        c.Tags,
        Json(c.CustomFields),
        c.MergedIntoId,
        c.Version,
        c.CreatedBy,
        c.CreatedAt,
        c.UpdatedAt,
        c.DeletedAt);

    public static ContactSummaryDto ToSummary(Contact c, LookupNames names) => new(
        c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.OwnerId, names.Owner(c.OwnerId));

    public static CustomFieldDefinitionDto ToDto(CustomFieldDefinition d) => new(
        d.Id,
        d.EntityType,
        d.FieldKey,
        d.Label,
        d.FieldType,
        ParseOptions(d.Options),
        d.IsRequired,
        d.SortOrder,
        d.IsActive);

    public static PicklistDto ToDto(Picklist p) => new(p.Id, p.ListType, p.Value, p.SortOrder, p.IsActive);

    public static OwnerDto ToDto(UserRef u) => new(u.UserId, u.DisplayName, u.IsActive);

    public static IReadOnlyList<string>? ParseOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<string[]>(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>A company's display name for a conflict or recycle-bin response.</summary>
    public static string DisplayName(Company c) => c.Name;

    /// <summary>A contact's display name. Used in conflict messages, never in logs (NFR-7).</summary>
    public static string DisplayName(Contact c)
        => string.IsNullOrWhiteSpace(c.LastName) ? c.FirstName : $"{c.FirstName} {c.LastName}";
}
