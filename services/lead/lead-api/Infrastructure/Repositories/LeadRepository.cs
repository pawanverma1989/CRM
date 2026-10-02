namespace LeadApi.Infrastructure.Repositories;
using LeadApi.Application.DTOs;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Every read goes through visibility rules (CLAUDE.md §6):
/// organization_id = claim AND deleted_at IS NULL AND (admin OR owner_id = ANY(visible_owner_ids) OR owner_id IS NULL for manager+).
/// </summary>
public interface ILeadRepository
{
    Task<Lead?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct);
    Task<(List<Lead> Items, int Total)> ListAsync(LeadListQuery query, IRequestContext ctx, CancellationToken ct);
    Task<List<Lead>> ListDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct);
    Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);
    Task<Lead?> GetDeletedAsync(Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);
    Task<Lead?> FindByEmailAsync(Guid organizationId, string email, Guid? excludeId, CancellationToken ct);
    Task<Lead?> FindByPhoneAsync(Guid organizationId, string phoneNormalized, Guid? excludeId, CancellationToken ct);
    Task<List<Lead>> GetByOwnerAsync(Guid organizationId, Guid ownerId, CancellationToken ct);
    Task<LeadConversion?> GetConversionAsync(Guid conversionId, Guid organizationId, CancellationToken ct);
    Task<LeadConversion?> GetConversionByLeadAsync(Guid leadId, CancellationToken ct);
    Task<List<LeadConversion>> ListPendingConversionsAsync(int maxRetries, CancellationToken ct);
    void Add(Lead lead);
    void AddConversion(LeadConversion conversion);
}

public class LeadRepository(LeadDbContext context, IOptions<LeadSettings> settings) : ILeadRepository
{
    private readonly LeadSettings _settings = settings.Value;

    public Task<Lead?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct)
        => VisibleLeads(ctx).FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<(List<Lead> Items, int Total)> ListAsync(
        LeadListQuery query, IRequestContext ctx, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? _settings.DefaultPageSize, 1, _settings.MaxPageSize);

        var filtered = Filter(VisibleLeads(ctx), query, ctx);
        var total = await filtered.CountAsync(ct);
        var items = await Sort(filtered, query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<List<Lead>> ListDeletedAsync(
        IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct)
        => context.DeletedLeadsQuery(ctx, restorableSince)
            .OrderByDescending(e => e.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => context.DeletedLeadsQuery(ctx, restorableSince).CountAsync(ct);

    public Task<Lead?> GetDeletedAsync(Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => context.DeletedLeadsQuery(ctx, restorableSince).FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Lead?> FindByEmailAsync(Guid organizationId, string email, Guid? excludeId, CancellationToken ct)
        => context.Leads
            .Where(e => e.OrganizationId == organizationId
                     && e.DeletedAt == null
                     && e.Email == email
                     && (excludeId == null || e.Id != excludeId))
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

    public Task<Lead?> FindByPhoneAsync(Guid organizationId, string phoneNormalized, Guid? excludeId, CancellationToken ct)
        => context.Leads
            .Where(e => e.OrganizationId == organizationId
                     && e.DeletedAt == null
                     && e.PhoneNormalized == phoneNormalized
                     && (excludeId == null || e.Id != excludeId))
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

    public Task<List<Lead>> GetByOwnerAsync(Guid organizationId, Guid ownerId, CancellationToken ct)
        => context.Leads
            .Where(e => e.OrganizationId == organizationId && e.OwnerId == ownerId && e.DeletedAt == null)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<LeadConversion?> GetConversionAsync(Guid conversionId, Guid organizationId, CancellationToken ct)
        => context.LeadConversions
            .FirstOrDefaultAsync(e => e.Id == conversionId && e.OrganizationId == organizationId, ct);

    public Task<LeadConversion?> GetConversionByLeadAsync(Guid leadId, CancellationToken ct)
        => context.LeadConversions.FirstOrDefaultAsync(e => e.LeadId == leadId, ct);

    public Task<List<LeadConversion>> ListPendingConversionsAsync(int maxRetries, CancellationToken ct)
        => context.LeadConversions
            .Where(e => e.Status != "completed" && e.Status != "compensated" && e.Status != "failed"
                     && e.Attempts < maxRetries
                     && (e.NextRetryAt == null || e.NextRetryAt <= DateTimeOffset.UtcNow))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

    public void Add(Lead lead) => context.Leads.Add(lead);
    public void AddConversion(LeadConversion conversion) => context.LeadConversions.Add(conversion);

    private IQueryable<Lead> VisibleLeads(IRequestContext ctx) => context.VisibleLeadsQuery(ctx);

    private IQueryable<Lead> Filter(IQueryable<Lead> query, LeadListQuery q, IRequestContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(q.Status)) query = query.Where(e => e.Status == q.Status);
        if (q.OwnerId.HasValue) query = query.Where(e => e.OwnerId == q.OwnerId.Value);
        if (q.LeadSourceId.HasValue) query = query.Where(e => e.LeadSourceId == q.LeadSourceId.Value);
        if (q.Unassigned == true) query = query.Where(e => e.OwnerId == null);
        if (!string.IsNullOrWhiteSpace(q.UtmCampaign)) query = query.Where(e => e.UtmCampaign == q.UtmCampaign);
        if (q.CreatedFrom.HasValue) query = query.Where(e => e.CreatedAt >= q.CreatedFrom.Value);
        if (q.CreatedTo.HasValue) query = query.Where(e => e.CreatedAt <= q.CreatedTo.Value);

        foreach (var raw in q.Tags ?? [])
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tag = raw.Trim().ToLowerInvariant();
            query = query.Where(e => e.Tags.Contains(tag));
        }

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            var like = $"%{term.Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(e =>
                (e.FirstName != null && EF.Functions.ILike(e.FirstName, like))
                || (e.LastName != null && EF.Functions.ILike(e.LastName, like))
                || (e.Email != null && EF.Functions.ILike(e.Email, like))
                || (e.CompanyName != null && EF.Functions.ILike(e.CompanyName, like))
                || (e.Phone != null && EF.Functions.ILike(e.Phone, like)));
        }

        return query;
    }

    private static IQueryable<Lead> Sort(IQueryable<Lead> query, LeadListQuery q)
    {
        var field = q.SortBy?.ToLowerInvariant() ?? "created";
        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return (field, desc) switch
        {
            ("created", false) => query.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
            ("created", true) => query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id),
            ("updated", false) => query.OrderBy(e => e.UpdatedAt).ThenBy(e => e.Id),
            ("updated", true) => query.OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Id),
            ("status", false) => query.OrderBy(e => e.Status).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id),
            ("status", true) => query.OrderByDescending(e => e.Status).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id),
            (_, true) => query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id),
            _ => query.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
        };
    }
}

/// <summary>Extension methods to expose the deleted-lead query for use outside the repository.</summary>
public static class LeadVisibilityQueries
{
    public static IQueryable<Lead> VisibleLeadsQuery(this LeadDbContext context, IRequestContext ctx)
    {
        var query = context.Leads.Where(e => e.OrganizationId == ctx.OrganizationId && e.DeletedAt == null);
        var owners = ctx.VisibleOwnerIds;

        if (owners is not null)
        {
            if (ctx.IsManagerOrAbove)
                query = query.Where(e => e.OwnerId == null || owners.Contains(e.OwnerId.Value));
            else
                query = query.Where(e => e.OwnerId != null && owners.Contains(e.OwnerId.Value));
        }

        return query;
    }

    public static IQueryable<Lead> DeletedLeadsQuery(this LeadDbContext context, IRequestContext ctx, DateTimeOffset restorableSince)
        => context.Leads.Where(e => e.OrganizationId == ctx.OrganizationId
                                 && e.DeletedAt != null
                                 && e.DeletedAt >= restorableSince);

    public static bool CanSeeOwner(this IRequestContext ctx, Guid? ownerId)
        => ownerId is null || ctx.VisibleOwnerIds is null || ctx.VisibleOwnerIds.Contains(ownerId.Value);
}
