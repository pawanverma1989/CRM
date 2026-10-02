namespace CustomerApi.Infrastructure.Repositories;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Queries;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Context;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>Contact reads and lookups. Visibility is applied by <see cref="VisibilityQueries"/> (§2, AC-7, AC-8).</summary>
public interface IContactRepository
{
    Task<Contact?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct);

    Task<List<Contact>> GetManyAsync(IReadOnlyCollection<Guid> ids, IRequestContext ctx, CancellationToken ct);

    Task<Contact?> GetDeletedAsync(Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);

    Task<(List<Contact> Items, int Total)> ListAsync(
        ContactListQuery query, IRequestContext ctx, CancellationToken ct, int? maxPageSize = null);

    Task<List<Contact>> ListDeletedAsync(
        IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct);

    Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);

    /// <summary>COM-2: the contacts shown on a company's detail page, within the caller's visibility.</summary>
    Task<List<Contact>> ForCompanyAsync(Guid companyId, IRequestContext ctx, CancellationToken ct);

    /// <summary>COM-5: every live contact linked to a company, regardless of owner, so none is left pointing at a deleted company.</summary>
    Task<List<Contact>> LinkedToCompanyAsync(Guid companyId, Guid organizationId, CancellationToken ct);

    /// <summary>
    /// DUP-1: the live contact holding <paramref name="email"/>, compared case-insensitively
    /// (<c>citext</c>, CON-3). Organization-wide, not owner-scoped, so the save is blocked and the
    /// existing record named even when the caller cannot see it (AC-3).
    /// </summary>
    Task<Contact?> FindLiveByEmailAsync(Guid organizationId, string email, Guid? excludeId, CancellationToken ct);

    /// <summary>CON-7 / AC-16: the contact a previous conversion of this lead already created.</summary>
    Task<Contact?> FindBySourceLeadAsync(Guid sourceLeadId, CancellationToken ct);

    /// <summary>DUP-2: live contacts sharing a normalized phone number (AC-5).</summary>
    Task<List<Contact>> FindByNormalizedPhoneAsync(
        Guid organizationId, string phoneNormalized, Guid? excludeId, int limit, CancellationToken ct);

    /// <summary>DUP-2: a very similar name at the same company (pg_trgm).</summary>
    Task<List<Contact>> FindSimilarByNameAsync(
        Guid organizationId, string fullName, Guid? companyId, Guid? excludeId,
        double threshold, int limit, CancellationToken ct);

    Task<List<Contact>> OwnedByAsync(Guid organizationId, Guid ownerId, CancellationToken ct);

    void Add(Contact contact);
}

public class ContactRepository(CustomerDbContext context, IOptions<CustomerSettings> settings) : IContactRepository
{
    private readonly CustomerSettings _settings = settings.Value;

    public Task<Contact?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct)
        => context.Contacts.VisibleContacts(ctx).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<List<Contact>> GetManyAsync(
        IReadOnlyCollection<Guid> ids, IRequestContext ctx, CancellationToken ct)
    {
        var idList = ids.Distinct().ToArray();
        return context.Contacts.VisibleContacts(ctx).Where(c => idList.Contains(c.Id)).ToListAsync(ct);
    }

    public Task<Contact?> GetDeletedAsync(
        Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => context.Contacts.DeletedContacts(ctx, restorableSince).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<(List<Contact> Items, int Total)> ListAsync(
        ContactListQuery query, IRequestContext ctx, CancellationToken ct, int? maxPageSize = null)
    {
        var (page, pageSize) = ListPaging.Resolve(query, _settings, maxPageSize);
        var filtered = Filter(context.Contacts.VisibleContacts(ctx), query);

        var total = await filtered.CountAsync(ct);
        var items = await Sort(filtered, query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<List<Contact>> ListDeletedAsync(
        IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct)
        => context.Contacts
            .DeletedContacts(ctx, restorableSince)
            .OrderByDescending(c => c.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => context.Contacts.DeletedContacts(ctx, restorableSince).CountAsync(ct);

    public Task<List<Contact>> ForCompanyAsync(Guid companyId, IRequestContext ctx, CancellationToken ct)
        => context.Contacts
            .VisibleContacts(ctx)
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<Contact>> LinkedToCompanyAsync(Guid companyId, Guid organizationId, CancellationToken ct)
        => context.Contacts
            .Where(c => c.CompanyId == companyId && c.OrganizationId == organizationId && c.DeletedAt == null)
            .ToListAsync(ct);

    public Task<Contact?> FindLiveByEmailAsync(
        Guid organizationId, string email, Guid? excludeId, CancellationToken ct)
        => context.Contacts
            .Where(c => c.OrganizationId == organizationId
                     && c.DeletedAt == null
                     && c.MergedIntoId == null
                     && c.Email == email
                     && (excludeId == null || c.Id != excludeId))
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

    public Task<Contact?> FindBySourceLeadAsync(Guid sourceLeadId, CancellationToken ct)
        => context.Contacts.FirstOrDefaultAsync(c => c.SourceLeadId == sourceLeadId, ct);

    public Task<List<Contact>> FindByNormalizedPhoneAsync(
        Guid organizationId, string phoneNormalized, Guid? excludeId, int limit, CancellationToken ct)
        => context.Contacts
            .Where(c => c.OrganizationId == organizationId
                     && c.DeletedAt == null
                     && c.MergedIntoId == null
                     && c.PhoneNormalized == phoneNormalized
                     && (excludeId == null || c.Id != excludeId))
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<Contact>> FindSimilarByNameAsync(
        Guid organizationId, string fullName, Guid? companyId, Guid? excludeId,
        double threshold, int limit, CancellationToken ct)
        => context.Contacts
            .Where(c => c.OrganizationId == organizationId
                     && c.DeletedAt == null
                     && c.MergedIntoId == null
                     && (excludeId == null || c.Id != excludeId)
                     && (companyId == null || c.CompanyId == companyId)
                     && EF.Functions.TrigramsSimilarity(
                            c.FirstName + " " + (c.LastName ?? ""), fullName) >= threshold)
            .OrderByDescending(c => EF.Functions.TrigramsSimilarity(
                c.FirstName + " " + (c.LastName ?? ""), fullName))
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<Contact>> OwnedByAsync(Guid organizationId, Guid ownerId, CancellationToken ct)
        => context.Contacts
            .Where(c => c.OrganizationId == organizationId && c.OwnerId == ownerId && c.DeletedAt == null)
            .AsNoTracking()
            .ToListAsync(ct);

    public void Add(Contact contact) => context.Contacts.Add(contact);

    private IQueryable<Contact> Filter(IQueryable<Contact> query, ContactListQuery q)
    {
        if (q.OwnerId is { } owner) query = query.Where(c => c.OwnerId == owner);
        if (q.CompanyId is { } company) query = query.Where(c => c.CompanyId == company);
        if (q.SourceId is { } source) query = query.Where(c => c.SourceId == source);
        if (!string.IsNullOrWhiteSpace(q.City)) query = query.Where(c => c.City == q.City);
        if (!string.IsNullOrWhiteSpace(q.State)) query = query.Where(c => c.State == q.State);
        if (!string.IsNullOrWhiteSpace(q.Country)) query = query.Where(c => c.Country == q.Country);
        if (q.CreatedFrom is { } cf) query = query.Where(c => c.CreatedAt >= cf);
        if (q.CreatedTo is { } ctd) query = query.Where(c => c.CreatedAt <= ctd);
        if (q.UpdatedFrom is { } uf) query = query.Where(c => c.UpdatedAt >= uf);
        if (q.UpdatedTo is { } ut) query = query.Where(c => c.UpdatedAt <= ut);

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
            var digits = new string([.. term.Where(char.IsAsciiDigit)]);
            var digitsLike = digits.Length >= 4 ? "%" + digits + "%" : null;
            var threshold = _settings.SimilarityThreshold;

            // LST-4: name, email, phone — partial, case-insensitive, and trigram-tolerant.
            query = query.Where(c =>
                EF.Functions.ILike(c.FirstName + " " + (c.LastName ?? ""), like)
                || (c.Email != null && EF.Functions.ILike(c.Email, like))
                || (digitsLike != null && c.PhoneNormalized != null && EF.Functions.ILike(c.PhoneNormalized, digitsLike))
                || (digitsLike != null && c.Mobile != null && EF.Functions.ILike(c.Mobile, digitsLike))
                || EF.Functions.TrigramsSimilarity(c.FirstName + " " + (c.LastName ?? ""), term) >= threshold);
        }

        return query;
    }

    private IQueryable<Contact> Sort(IQueryable<Contact> query, ContactListQuery q)
    {
        var (field, desc) = ListPaging.ResolveSort(q);
        var owners = context.UserRefs;

        return (field, desc) switch
        {
            ("created", false) => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            ("created", true) => query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id),
            ("updated", false) => query.OrderBy(c => c.UpdatedAt).ThenBy(c => c.Id),
            ("updated", true) => query.OrderByDescending(c => c.UpdatedAt).ThenBy(c => c.Id),
            ("owner", false) => query
                .OrderBy(c => owners.Where(u => u.UserId == c.OwnerId).Select(u => u.DisplayName).FirstOrDefault())
                .ThenBy(c => c.FirstName).ThenBy(c => c.Id),
            ("owner", true) => query
                .OrderByDescending(c => owners.Where(u => u.UserId == c.OwnerId).Select(u => u.DisplayName).FirstOrDefault())
                .ThenBy(c => c.FirstName).ThenBy(c => c.Id),
            (_, true) => query.OrderByDescending(c => c.FirstName).ThenByDescending(c => c.LastName).ThenBy(c => c.Id),
            _ => query.OrderBy(c => c.FirstName).ThenBy(c => c.LastName).ThenBy(c => c.Id)
        };
    }
}
