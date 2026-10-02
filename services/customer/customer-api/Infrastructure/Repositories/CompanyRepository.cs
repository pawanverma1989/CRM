namespace CustomerApi.Infrastructure.Repositories;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Queries;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Every read goes through <see cref="VisibilityQueries"/>, so a record outside the caller's
/// visibility simply is not found — which the service turns into a 404, never a 403 (§2, AC-7).
/// </summary>
public interface ICompanyRepository
{
    Task<Company?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct);

    /// <summary>Used by merge and bulk actions, which need the tracked entity.</summary>
    Task<List<Company>> GetManyAsync(IReadOnlyCollection<Guid> ids, IRequestContext ctx, CancellationToken ct);

    /// <summary>Recycle bin lookup, limited to the retention window (DEL-2, AC-15).</summary>
    Task<Company?> GetDeletedAsync(Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);

    Task<(List<Company> Items, int Total)> ListAsync(
        CompanyListQuery query, IRequestContext ctx, CancellationToken ct, int? maxPageSize = null);

    Task<List<Company>> ListDeletedAsync(
        IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct);

    Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);

    /// <summary>
    /// DUP-1: the live company holding <paramref name="domain"/>, organization-wide and
    /// deliberately not owner-scoped — the save must be blocked, and named, even when the
    /// conflicting record belongs to someone the caller cannot see (AC-4).
    /// </summary>
    Task<Company?> FindLiveByDomainAsync(
        Guid organizationId, string domain, Guid? excludeId, CancellationToken ct);

    /// <summary>DUP-3: companies with a very similar name (pg_trgm).</summary>
    Task<List<Company>> FindSimilarByNameAsync(
        Guid organizationId, string name, Guid? excludeId, double threshold, int limit, CancellationToken ct);

    Task<List<Company>> OwnedByAsync(Guid organizationId, Guid ownerId, CancellationToken ct);

    void Add(Company company);
}

public class CompanyRepository(CustomerDbContext context, IOptions<CustomerSettings> settings) : ICompanyRepository
{
    private readonly CustomerSettings _settings = settings.Value;

    public Task<Company?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct)
        => context.Companies.VisibleCompanies(ctx).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Company>> GetManyAsync(
        IReadOnlyCollection<Guid> ids, IRequestContext ctx, CancellationToken ct)
    {
        var idList = ids.Distinct().ToArray();
        return context.Companies.VisibleCompanies(ctx).Where(c => idList.Contains(c.Id)).ToListAsync(ct);
    }

    public Task<Company?> GetDeletedAsync(
        Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => context.Companies
            .DeletedCompanies(ctx, restorableSince)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<(List<Company> Items, int Total)> ListAsync(
        CompanyListQuery query, IRequestContext ctx, CancellationToken ct, int? maxPageSize = null)
    {
        var (page, pageSize) = ListPaging.Resolve(query, _settings, maxPageSize);
        var filtered = Filter(context.Companies.VisibleCompanies(ctx), query);

        var total = await filtered.CountAsync(ct);
        var items = await Sort(filtered, query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<List<Company>> ListDeletedAsync(
        IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct)
        => context.Companies
            .DeletedCompanies(ctx, restorableSince)
            .OrderByDescending(c => c.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => context.Companies.DeletedCompanies(ctx, restorableSince).CountAsync(ct);

    public Task<Company?> FindLiveByDomainAsync(
        Guid organizationId, string domain, Guid? excludeId, CancellationToken ct)
        => context.Companies
            .Where(c => c.OrganizationId == organizationId
                     && c.DeletedAt == null
                     && c.MergedIntoId == null
                     && c.Domain == domain
                     && (excludeId == null || c.Id != excludeId))
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

    public Task<List<Company>> FindSimilarByNameAsync(
        Guid organizationId, string name, Guid? excludeId, double threshold, int limit, CancellationToken ct)
        => context.Companies
            .Where(c => c.OrganizationId == organizationId
                     && c.DeletedAt == null
                     && c.MergedIntoId == null
                     && (excludeId == null || c.Id != excludeId)
                     && EF.Functions.TrigramsSimilarity(c.Name, name) >= threshold)
            .OrderByDescending(c => EF.Functions.TrigramsSimilarity(c.Name, name))
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<Company>> OwnedByAsync(Guid organizationId, Guid ownerId, CancellationToken ct)
        => context.Companies
            .Where(c => c.OrganizationId == organizationId && c.OwnerId == ownerId && c.DeletedAt == null)
            .AsNoTracking()
            .ToListAsync(ct);

    public void Add(Company company) => context.Companies.Add(company);

    private IQueryable<Company> Filter(IQueryable<Company> query, CompanyListQuery q)
    {
        if (q.OwnerId is { } owner) query = query.Where(c => c.OwnerId == owner);
        if (q.IndustryId is { } industry) query = query.Where(c => c.IndustryId == industry);
        if (!string.IsNullOrWhiteSpace(q.City)) query = query.Where(c => c.City == q.City);
        if (!string.IsNullOrWhiteSpace(q.State)) query = query.Where(c => c.State == q.State);
        if (!string.IsNullOrWhiteSpace(q.Country)) query = query.Where(c => c.Country == q.Country);
        if (q.CreatedFrom is { } cf) query = query.Where(c => c.CreatedAt >= cf);
        if (q.CreatedTo is { } ctd) query = query.Where(c => c.CreatedAt <= ctd);
        if (q.UpdatedFrom is { } uf) query = query.Where(c => c.UpdatedAt >= uf);
        if (q.UpdatedTo is { } ut) query = query.Where(c => c.UpdatedAt <= ut);

        // A record must carry every tag asked for; one Where per tag keeps the GIN index usable.
        foreach (var raw in q.Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tag = raw.Trim().ToLowerInvariant();
            query = query.Where(c => c.Tags.Contains(tag));
        }

        foreach (var (key, value) in ListPaging.CustomFieldFilters(q.CustomField))
        {
            var (scalar, array) = ListPaging.CustomFieldJson(key, value);
            query = query.Where(c => EF.Functions.JsonContains(c.CustomFields, scalar)
                                  || EF.Functions.JsonContains(c.CustomFields, array));
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            var like = ListPaging.LikePattern(term);
            var threshold = _settings.SimilarityThreshold;

            // LST-4: partial matches on name, phone and domain, plus trigram similarity so a
            // slightly misspelled name still finds the record.
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, like)
                || (c.Domain != null && EF.Functions.ILike(c.Domain, like))
                || (c.Phone != null && EF.Functions.ILike(c.Phone, like))
                || EF.Functions.TrigramsSimilarity(c.Name, term) >= threshold);
        }

        return query;
    }

    private IQueryable<Company> Sort(IQueryable<Company> query, CompanyListQuery q)
    {
        var (field, desc) = ListPaging.ResolveSort(q);

        // LST-3 sorts by owner name, which lives in the local user_refs copy — a correlated
        // subquery keeps it one round trip and avoids calling Identity.
        var owners = context.UserRefs;

        return (field, desc) switch
        {
            ("created", false) => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            ("created", true) => query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id),
            ("updated", false) => query.OrderBy(c => c.UpdatedAt).ThenBy(c => c.Id),
            ("updated", true) => query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id),
            ("owner", false) => query
                .OrderBy(c => owners.Where(u => u.UserId == c.OwnerId).Select(u => u.DisplayName).FirstOrDefault())
                .ThenBy(c => c.Name).ThenBy(c => c.Id),
            ("owner", true) => query
                .OrderByDescending(c => owners.Where(u => u.UserId == c.OwnerId).Select(u => u.DisplayName).FirstOrDefault())
                .ThenBy(c => c.Name).ThenBy(c => c.Id),
            (_, true) => query.OrderByDescending(c => c.Name).ThenBy(c => c.Id),
            _ => query.OrderBy(c => c.Name).ThenBy(c => c.Id)
        };
    }
}
