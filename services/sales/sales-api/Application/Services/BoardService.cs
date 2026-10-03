namespace SalesApi.Application.Services;
using SalesApi.Application.DTOs;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Kanban board view (BRD-1..BRD-5). Returns open deals grouped by stage with totals and
/// weighted forecast. Uses local customer_refs and user_refs to avoid cross-service calls (NFR-4).
/// </summary>
public interface IBoardService
{
    Task<BoardResponse> GetBoardAsync(Guid pipelineId, CancellationToken ct);
}

public class BoardService(
    SalesDbContext context,
    ILookupRepository lookup,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<SalesSettings> settings) : IBoardService
{
    private readonly SalesSettings _settings = settings.Value;

    public async Task<BoardResponse> GetBoardAsync(Guid pipelineId, CancellationToken ct)
    {
        var pipeline = await lookup.GetPipelineAsync(pipelineId, ctx.OrganizationId, ct)
            ?? throw new NotFoundException($"No pipeline with id {pipelineId}.");

        var activeStages = pipeline.Stages
            .Where(s => s.IsActive && s.StageType == "open")
            .OrderBy(s => s.SortOrder)
            .ToList();

        // Load open, visible deals for this pipeline
        var dealsQuery = context.Deals
            .Where(d => d.OrganizationId == ctx.OrganizationId
                     && d.PipelineId == pipelineId
                     && d.Status == "open"
                     && d.DeletedAt == null);

        // Apply visibility filter
        var owners = ctx.VisibleOwnerIds;
        if (owners is not null)
        {
            if (ctx.IsManagerOrAbove)
                dealsQuery = dealsQuery.Where(d => d.OwnerId == null || owners.Contains(d.OwnerId.Value));
            else
                dealsQuery = dealsQuery.Where(d => d.OwnerId != null && owners.Contains(d.OwnerId.Value));
        }

        var rawDeals = await dealsQuery.AsNoTracking().ToListAsync(ct);

        // Load company refs and user refs in bulk for the board deals
        var companyIds = rawDeals.Where(d => d.CompanyId.HasValue).Select(d => d.CompanyId!.Value).Distinct().ToArray();
        var ownerIds = rawDeals.Where(d => d.OwnerId.HasValue).Select(d => d.OwnerId!.Value).Distinct().ToArray();

        var companyRefs = companyIds.Length > 0
            ? await context.CustomerRefs
                .Where(r => r.EntityType == "company" && companyIds.Contains(r.Id))
                .AsNoTracking()
                .ToDictionaryAsync(r => r.Id, ct)
            : new Dictionary<Guid, Domain.Entities.CustomerRef>();

        var userRefs = ownerIds.Length > 0
            ? await context.UserRefs
                .Where(u => ownerIds.Contains(u.UserId))
                .AsNoTracking()
                .ToDictionaryAsync(u => u.UserId, ct)
            : new Dictionary<Guid, Domain.Entities.UserRef>();

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var dealsByStage = rawDeals.GroupBy(d => d.StageId).ToDictionary(g => g.Key, g => g.ToList());

        var columns = activeStages.Select(stage =>
        {
            var stageDeals = dealsByStage.GetValueOrDefault(stage.Id, []);

            var boardDeals = stageDeals.Select(d =>
            {
                var daysInStage = (int)(now - d.StageEnteredAt).TotalDays;
                var isStale = IsStale(d, now);
                var isOverdue = d.ExpectedCloseDate.HasValue && d.ExpectedCloseDate.Value < today;
                var probability = (short)(d.Probability ?? stage.Probability);

                companyRefs.TryGetValue(d.CompanyId ?? Guid.Empty, out var companyRef);
                userRefs.TryGetValue(d.OwnerId ?? Guid.Empty, out var ownerRef);

                return new BoardDealDto(
                    d.Id,
                    d.Name,
                    companyRef?.DisplayName,
                    d.Amount,
                    d.Currency,
                    d.OwnerId,
                    ownerRef?.DisplayName,
                    d.ExpectedCloseDate,
                    daysInStage,
                    isStale,
                    isOverdue,
                    probability,
                    d.Tags);
            }).ToList();

            var totalAmount = stageDeals.Sum(d => d.Amount);
            var weightedForecast = stageDeals.Sum(d =>
                d.Amount * (decimal)(d.Probability ?? stage.Probability) / 100m);

            return new BoardColumnDto(
                stage.Id,
                stage.Name,
                stage.SortOrder,
                boardDeals.Count,
                totalAmount,
                weightedForecast,
                boardDeals);
        }).ToList();

        var pipelineDto = new BoardPipelineDto(
            pipeline.Id,
            pipeline.Name,
            [.. pipeline.Stages.OrderBy(s => s.SortOrder).Select(s =>
                new PipelineStageDto(s.Id, s.PipelineId, s.Name, s.SortOrder, s.Probability, s.StageType, s.IsActive))]);

        return new BoardResponse(pipelineDto, columns);
    }

    private bool IsStale(Domain.Entities.Deal deal, DateTimeOffset now)
    {
        if (deal.LastActivityAt.HasValue &&
            (now - deal.LastActivityAt.Value).TotalDays > _settings.StaleDaysWithoutActivity)
            return true;
        if ((now - deal.StageEnteredAt).TotalDays > _settings.StaleDaysInStage)
            return true;
        return false;
    }
}
