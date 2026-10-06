namespace SalesApi.Infrastructure.Repositories;
using SalesApi.Application.DTOs;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Every read goes through visibility rules (CLAUDE.md §6):
/// organization_id = claim AND deleted_at IS NULL AND (admin OR owner_id = ANY(visible_owner_ids)).
/// </summary>
public interface IDealRepository
{
    Task<Deal?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct);
    Task<Deal?> GetWithDetailsAsync(Guid id, IRequestContext ctx, CancellationToken ct);
    Task<(List<Deal> Items, int Total)> ListAsync(DealListQuery query, IRequestContext ctx, CancellationToken ct);
    Task<Deal?> FindBySourceLeadIdAsync(Guid sourceLeadId, Guid organizationId, CancellationToken ct);
    Task<List<Deal>> GetByOwnerAsync(Guid organizationId, Guid ownerId, CancellationToken ct);
    Task<List<Deal>> ListDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct);
    Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);
    Task<Deal?> GetDeletedAsync(Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct);
    Task<List<ReassignmentQueue>> GetReassignmentQueueAsync(Guid organizationId, CancellationToken ct);
    Task<bool> HasOpenDealsInStageAsync(Guid stageId, CancellationToken ct);
    void Add(Deal deal);
}

/// <summary>Extension methods for visibility queries.</summary>
public static class DealVisibilityExtensions
{
    public static bool CanSeeOwner(this IRequestContext ctx, Guid? ownerId)
        => ownerId is null || ctx.VisibleOwnerIds is null || ctx.VisibleOwnerIds.Contains(ownerId.Value);
}

public class DealRepository(SalesDbContext context, IOptions<SalesSettings> settings) : IDealRepository
{
    private readonly SalesSettings _settings = settings.Value;

    public Task<Deal?> GetAsync(Guid id, IRequestContext ctx, CancellationToken ct)
        => VisibleDeals(ctx).FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Deal?> GetWithDetailsAsync(Guid id, IRequestContext ctx, CancellationToken ct)
        => VisibleDeals(ctx)
            .Include(d => d.DealContacts)
            .Include(d => d.StageHistory.OrderByDescending(h => h.ChangedAt).Take(20))
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<(List<Deal> Items, int Total)> ListAsync(
        DealListQuery query, IRequestContext ctx, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? _settings.DefaultPageSize, 1, _settings.MaxPageSize);

        var filtered = Filter(VisibleDeals(ctx), query);
        var total = await filtered.CountAsync(ct);
        var items = await Sort(filtered, query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<Deal?> FindBySourceLeadIdAsync(Guid sourceLeadId, Guid organizationId, CancellationToken ct)
        => context.Deals
            .FirstOrDefaultAsync(e => e.SourceLeadId == sourceLeadId && e.OrganizationId == organizationId, ct);

    public Task<List<Deal>> GetByOwnerAsync(Guid organizationId, Guid ownerId, CancellationToken ct)
        => context.Deals
            .Where(e => e.OrganizationId == organizationId && e.OwnerId == ownerId && e.DeletedAt == null)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<List<Deal>> ListDeletedAsync(
        IRequestContext ctx, DateTimeOffset restorableSince, int page, int pageSize, CancellationToken ct)
        => DeletedDeals(ctx, restorableSince)
            .OrderByDescending(e => e.DeletedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<int> CountDeletedAsync(IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => DeletedDeals(ctx, restorableSince).CountAsync(ct);

    public Task<Deal?> GetDeletedAsync(Guid id, IRequestContext ctx, DateTimeOffset restorableSince, CancellationToken ct)
        => DeletedDeals(ctx, restorableSince).FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<List<ReassignmentQueue>> GetReassignmentQueueAsync(Guid organizationId, CancellationToken ct)
        => context.ReassignmentQueue
            .Include(r => r.Deal)
            .Where(r => r.OrganizationId == organizationId)
            .OrderBy(r => r.QueuedAt)
            .AsNoTracking()
            .ToListAsync(ct);

    public Task<bool> HasOpenDealsInStageAsync(Guid stageId, CancellationToken ct)
        => context.Deals
            .AnyAsync(d => d.StageId == stageId && d.Status == "open" && d.DeletedAt == null, ct);

    public void Add(Deal deal) => context.Deals.Add(deal);

    private IQueryable<Deal> VisibleDeals(IRequestContext ctx)
    {
        var query = context.Deals.Where(e => e.OrganizationId == ctx.OrganizationId && e.DeletedAt == null);
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

    private IQueryable<Deal> DeletedDeals(IRequestContext ctx, DateTimeOffset restorableSince)
        => context.Deals.Where(e => e.OrganizationId == ctx.OrganizationId
                                 && e.DeletedAt != null
                                 && e.DeletedAt >= restorableSince);

    private IQueryable<Deal> Filter(IQueryable<Deal> query, DealListQuery q)
    {
        if (q.PipelineId.HasValue) query = query.Where(e => e.PipelineId == q.PipelineId.Value);
        if (q.StageId.HasValue) query = query.Where(e => e.StageId == q.StageId.Value);
        if (q.OwnerId.HasValue) query = query.Where(e => e.OwnerId == q.OwnerId.Value);
        if (!string.IsNullOrWhiteSpace(q.Status)) query = query.Where(e => e.Status == q.Status);
        if (q.Unassigned == true) query = query.Where(e => e.OwnerId == null);
        if (q.CloseDateFrom.HasValue) query = query.Where(e => e.ExpectedCloseDate >= q.CloseDateFrom.Value);
        if (q.CloseDateTo.HasValue) query = query.Where(e => e.ExpectedCloseDate <= q.CloseDateTo.Value);
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
            query = query.Where(e => EF.Functions.ILike(e.Name, like));
        }

        return query;
    }

    private static IQueryable<Deal> Sort(IQueryable<Deal> query, DealListQuery q)
    {
        var field = q.SortBy?.ToLowerInvariant() ?? "created";
        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        return (field, desc) switch
        {
            ("created", false) => query.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
            ("created", true) => query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id),
            ("updated", false) => query.OrderBy(e => e.UpdatedAt).ThenBy(e => e.Id),
            ("updated", true) => query.OrderByDescending(e => e.UpdatedAt).ThenBy(e => e.Id),
            ("amount", false) => query.OrderBy(e => e.Amount).ThenBy(e => e.Id),
            ("amount", true) => query.OrderByDescending(e => e.Amount).ThenBy(e => e.Id),
            ("close", false) => query.OrderBy(e => e.ExpectedCloseDate).ThenBy(e => e.Id),
            ("close", true) => query.OrderByDescending(e => e.ExpectedCloseDate).ThenBy(e => e.Id),
            (_, true) => query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id),
            _ => query.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
        };
    }
}
