namespace CustomerApi.Application.Services;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Repositories;
using CustomerApi.Settings;
using Microsoft.Extensions.Options;

/// <summary>Loads the <see cref="WriteReference"/> a write needs: active custom fields and the two picklists.</summary>
public interface IReferenceDataLoader
{
    Task<WriteReference> ForAsync(string entityType, CancellationToken ct);
}

public class ReferenceDataLoader(
    ILookupRepository lookups,
    IRequestContext ctx,
    IOptions<CustomerSettings> settings) : IReferenceDataLoader
{
    public async Task<WriteReference> ForAsync(string entityType, CancellationToken ct)
    {
        var customFields = await lookups.CustomFieldsAsync(ctx.OrganizationId, entityType, includeInactive: false, ct);
        var industries = await lookups.PicklistAsync(ctx.OrganizationId, PicklistTypes.CompanyIndustry, false, ct);
        var sources = await lookups.PicklistAsync(ctx.OrganizationId, PicklistTypes.ContactSource, false, ct);

        return new WriteReference(
            ctx.OrganizationId,
            customFields,
            industries.Select(p => p.Id).ToHashSet(),
            sources.Select(p => p.Id).ToHashSet(),
            settings.Value);
    }
}

/// <summary>The two value lists in <c>picklists.list_type</c> (V2 CHECK).</summary>
public static class PicklistTypes
{
    public const string CompanyIndustry = "company_industry";
    public const string ContactSource = "contact_source";

    public static readonly string[] All = [CompanyIndustry, ContactSource];

    /// <summary>§10 open question "which values should the lists start with" — these are the agreed defaults.</summary>
    public static readonly string[] DefaultIndustries =
    [
        "Technology", "Manufacturing", "Retail", "Healthcare", "Financial Services",
        "Education", "Real Estate", "Logistics", "Media", "Other"
    ];

    public static readonly string[] DefaultSources =
    [
        "Website", "Referral", "Event", "Import", "Cold Call", "Partner", "Other"
    ];

    public static string[] Defaults(string listType) => listType switch
    {
        CompanyIndustry => DefaultIndustries,
        ContactSource => DefaultSources,
        _ => []
    };
}

/// <summary>The two values <c>entity_type</c> may take (V1 CHECK).</summary>
public static class EntityTypes
{
    public const string Company = "company";
    public const string Contact = "contact";

    public static readonly string[] All = [Company, Contact];

    public static string Require(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return All.Contains(value)
            ? value
            : throw new Exceptions.CustomerValidationException("entityType", "Must be 'company' or 'contact'.");
    }
}
