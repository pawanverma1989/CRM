namespace CustomerApi.Infrastructure.Repositories;
using CustomerApi.Application.Mapping;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// The small, shared read models: picklist values, custom field definitions, the local
/// <c>user_refs</c> copy and tag suggestions. Everything is scoped to one organization.
/// </summary>
public interface ILookupRepository
{
    Task<List<CustomFieldDefinition>> CustomFieldsAsync(
        Guid organizationId, string entityType, bool includeInactive, CancellationToken ct);

    Task<CustomFieldDefinition?> CustomFieldAsync(Guid id, Guid organizationId, CancellationToken ct);

    Task<int> ActiveCustomFieldCountAsync(Guid organizationId, string entityType, CancellationToken ct);

    Task<List<Picklist>> PicklistAsync(
        Guid organizationId, string listType, bool includeInactive, CancellationToken ct);

    Task<Picklist?> PicklistValueAsync(Guid id, Guid organizationId, CancellationToken ct);

    /// <summary>True when the id is an active value of <paramref name="listType"/> in this organization.</summary>
    Task<bool> PicklistValueExistsAsync(Guid id, Guid organizationId, string listType, CancellationToken ct);

    Task<List<UserRef>> OwnersAsync(Guid organizationId, bool activeOnly, CancellationToken ct);

    /// <summary>TAG-3: existing tags on companies and contacts, optionally narrowed by prefix.</summary>
    Task<List<string>> TagSuggestionsAsync(Guid organizationId, string? prefix, int limit, CancellationToken ct);

    /// <summary>Resolves the display names a list or detail response needs, in one round trip each.</summary>
    Task<LookupNames> NamesAsync(
        Guid organizationId,
        IEnumerable<Guid> ownerIds,
        IEnumerable<Guid> picklistIds,
        IEnumerable<Guid> companyIds,
        CancellationToken ct);
}

public class LookupRepository(CustomerDbContext context) : ILookupRepository
{
    public Task<List<CustomFieldDefinition>> CustomFieldsAsync(
        Guid organizationId, string entityType, bool includeInactive, CancellationToken ct)
    {
        var query = context.CustomFieldDefinitions
            .Where(d => d.OrganizationId == organizationId && d.EntityType == entityType);

        if (!includeInactive) query = query.Where(d => d.IsActive);

        return query
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Label)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task<CustomFieldDefinition?> CustomFieldAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.CustomFieldDefinitions
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == organizationId, ct);

    public Task<int> ActiveCustomFieldCountAsync(Guid organizationId, string entityType, CancellationToken ct)
        => context.CustomFieldDefinitions
            .CountAsync(d => d.OrganizationId == organizationId && d.EntityType == entityType && d.IsActive, ct);

    public Task<List<Picklist>> PicklistAsync(
        Guid organizationId, string listType, bool includeInactive, CancellationToken ct)
    {
        var query = context.Picklists
            .Where(p => p.OrganizationId == organizationId && p.ListType == listType);

        if (!includeInactive) query = query.Where(p => p.IsActive);

        return query
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Value)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public Task<Picklist?> PicklistValueAsync(Guid id, Guid organizationId, CancellationToken ct)
        => context.Picklists.FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == organizationId, ct);

    public Task<bool> PicklistValueExistsAsync(Guid id, Guid organizationId, string listType, CancellationToken ct)
        => context.Picklists.AnyAsync(
            p => p.Id == id && p.OrganizationId == organizationId && p.ListType == listType && p.IsActive, ct);

    public Task<List<UserRef>> OwnersAsync(Guid organizationId, bool activeOnly, CancellationToken ct)
    {
        var query = context.UserRefs.Where(u => u.OrganizationId == organizationId);
        if (activeOnly) query = query.Where(u => u.IsActive);
        return query.OrderBy(u => u.DisplayName).AsNoTracking().ToListAsync(ct);
    }

    public async Task<List<string>> TagSuggestionsAsync(
        Guid organizationId, string? prefix, int limit, CancellationToken ct)
    {
        // tags live in two TEXT[] columns, so one UNION over unnest() is cheaper than two
        // round trips plus a client-side merge. Both GIN indexes stay usable.
        // An empty pattern rather than NULL: a null string parameter would leave Npgsql without a
        // type to send, and "pattern = ''" expresses "no prefix given" just as well.
        var pattern = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : prefix.Trim().ToLowerInvariant()
                    .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        return await context.Database
            .SqlQuery<string>($"""
                SELECT DISTINCT s.tag AS "Value"
                FROM (
                    SELECT unnest(tags) AS tag FROM companies
                     WHERE organization_id = {organizationId} AND deleted_at IS NULL
                    UNION ALL
                    SELECT unnest(tags) AS tag FROM contacts
                     WHERE organization_id = {organizationId} AND deleted_at IS NULL
                ) s
                WHERE {pattern} = '' OR s.tag LIKE {pattern}
                ORDER BY 1
                LIMIT {limit}
                """)
            .ToListAsync(ct);
    }

    public async Task<LookupNames> NamesAsync(
        Guid organizationId,
        IEnumerable<Guid> ownerIds,
        IEnumerable<Guid> picklistIds,
        IEnumerable<Guid> companyIds,
        CancellationToken ct)
    {
        var owners = ownerIds.Distinct().ToArray();
        var picklists = picklistIds.Distinct().ToArray();
        var companies = companyIds.Distinct().ToArray();

        var ownerNames = owners.Length == 0
            ? []
            : await context.UserRefs
                .Where(u => u.OrganizationId == organizationId && owners.Contains(u.UserId))
                .AsNoTracking()
                .ToDictionaryAsync(u => u.UserId, u => u.DisplayName, ct);

        var picklistNames = picklists.Length == 0
            ? []
            : await context.Picklists
                .Where(p => p.OrganizationId == organizationId && picklists.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => p.Value, ct);

        var companyNames = companies.Length == 0
            ? []
            : await context.Companies
                .Where(c => c.OrganizationId == organizationId && companies.Contains(c.Id))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return new LookupNames
        {
            Owners = ownerNames,
            PicklistValues = picklistNames,
            CompanyNames = companyNames
        };
    }
}
