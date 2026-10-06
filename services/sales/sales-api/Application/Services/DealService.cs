namespace SalesApi.Application.Services;
using System.Text.Json;
using SalesApi.Application.DTOs;
using SalesApi.Application.Events;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Deal CRUD: create, update, move, won/lost/reopen, delete, reassign (DL-1..DL-6, WL-1..WL-3).
/// </summary>
public interface IDealService
{
    Task<PagedResult<DealDto>> ListAsync(DealListQuery query, CancellationToken ct);
    Task<DealDto> GetAsync(Guid id, CancellationToken ct);
    Task<DealDto> CreateAsync(CreateDealRequest request, CancellationToken ct);
    Task<DealDto> UpdateAsync(Guid id, UpdateDealRequest request, CancellationToken ct);
    Task<DealDto> MoveAsync(Guid id, MoveDealRequest request, CancellationToken ct);
    Task<DealDto> MarkWonAsync(Guid id, MarkWonRequest request, CancellationToken ct);
    Task<DealDto> MarkLostAsync(Guid id, MarkLostRequest request, CancellationToken ct);
    Task<DealDto> ReopenAsync(Guid id, ReopenDealRequest request, CancellationToken ct);
    Task<DealDto> ReplaceContactsAsync(Guid id, ReplaceDealContactsRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<BulkReassignResult> ReassignAsync(BulkReassignRequest request, CancellationToken ct);
}

public class DealService(
    IDealRepository deals,
    ILookupRepository lookup,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<SalesSettings> settings,
    ILogger<DealService> logger) : IDealService
{
    private readonly SalesSettings _settings = settings.Value;

    public async Task<PagedResult<DealDto>> ListAsync(DealListQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? _settings.DefaultPageSize, 1, _settings.MaxPageSize);

        var (items, total) = await deals.ListAsync(query, ctx, ct);
        var dtos = await Task.WhenAll(items.Select(d => ToDtoAsync(d, false, ct)));
        return new PagedResult<DealDto>([.. dtos], page, pageSize, total);
    }

    public async Task<DealDto> GetAsync(Guid id, CancellationToken ct)
    {
        var deal = await deals.GetWithDetailsAsync(id, ctx, ct) ?? throw NotFound(id);
        return await ToDtoAsync(deal, true, ct);
    }

    public async Task<DealDto> CreateAsync(CreateDealRequest request, CancellationToken ct)
    {
        // DL-6: source_lead_id idempotency
        if (request.SourceLeadId.HasValue)
        {
            var existing = await deals.FindBySourceLeadIdAsync(request.SourceLeadId.Value, ctx.OrganizationId, ct);
            if (existing is not null)
            {
                logger.LogInformation("Deal for source lead {LeadId} already exists: {DealId}.", request.SourceLeadId, existing.Id);
                return await ToDtoAsync(existing, false, ct);
            }
        }

        // Validate pipeline/stage belong to this org
        var pipeline = await lookup.GetPipelineAsync(request.PipelineId, ctx.OrganizationId, ct)
            ?? throw new SalesValidationException("pipelineId", "Pipeline not found.");

        var stage = pipeline.Stages.FirstOrDefault(s => s.Id == request.StageId)
            ?? throw new SalesValidationException("stageId", "Stage does not belong to the specified pipeline.");

        if (!stage.IsActive)
            throw new SalesValidationException("stageId", "The specified stage is not active.");

        var now = clock.GetUtcNow();

        var deal = new Deal
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            PipelineId = request.PipelineId,
            StageId = request.StageId,
            OwnerId = ctx.IsService ? request.OwnerId : PermissionGuard.ResolveOwnerOnCreate(ctx, request.OwnerId),
            CompanyId = request.CompanyId,
            PrimaryContactId = request.PrimaryContactId,
            Name = request.Name.Trim(),
            Amount = request.Amount,
            Currency = request.Currency,
            Probability = request.Probability,
            ExpectedCloseDate = request.ExpectedCloseDate,
            Tags = NormalizeTags(request.Tags),
            CustomFields = SerializeCustomFields(request.CustomFields),
            SourceLeadId = request.SourceLeadId,
            Version = 1,
            CreatedBy = ctx.ActorUserId,
            CreatedAt = now,
            UpdatedAt = now,
            StageEnteredAt = now
        };

        // set_config is used because SET LOCAL does not accept parameterized values.
        await unitOfWork.ExecuteSqlAsync(
            $"SELECT set_config('app.current_user_id', {ctx.ActorUserId.ToString()}, true)", ct);

        deals.Add(deal);
        // deal.created is written by DB trigger (deals_log_stage on INSERT)
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Deal {Id} '{Name}' created in pipeline {PipelineId}.", deal.Id, deal.Name, deal.PipelineId);
        return await ToDtoAsync(deal, false, ct);
    }

    public async Task<DealDto> UpdateAsync(Guid id, UpdateDealRequest request, CancellationToken ct)
    {
        var deal = await deals.GetAsync(id, ctx, ct) ?? throw NotFound(id);

        PermissionGuard.EnsureCanEdit(ctx, deal.OwnerId);

        // DL-5: optimistic locking
        if (request.Version != deal.Version)
            throw new ConflictException(
                $"This deal has changed since you loaded it (version {deal.Version}, you sent {request.Version}).");

        if (request.OwnerId.HasValue && request.OwnerId.Value != deal.OwnerId)
            PermissionGuard.EnsureCanAssignOwner(ctx, request.OwnerId.Value);

        var now = clock.GetUtcNow();

        if (request.Name is not null) deal.Name = request.Name.Trim();
        if (request.Amount.HasValue) deal.Amount = request.Amount.Value;
        if (request.Currency is not null) deal.Currency = request.Currency;
        if (request.OwnerId.HasValue) deal.OwnerId = request.OwnerId.Value;
        if (request.CompanyId.HasValue) deal.CompanyId = request.CompanyId.Value;
        if (request.PrimaryContactId.HasValue) deal.PrimaryContactId = request.PrimaryContactId.Value;
        if (request.Probability.HasValue) deal.Probability = request.Probability.Value;
        if (request.ExpectedCloseDate.HasValue) deal.ExpectedCloseDate = request.ExpectedCloseDate.Value;
        if (request.Tags is not null) deal.Tags = NormalizeTags(request.Tags);
        if (request.CustomFields is not null) deal.CustomFields = SerializeCustomFields(request.CustomFields);
        if (request.LossNotes is not null) deal.LossNotes = request.LossNotes;
        deal.UpdatedAt = now;
        // version is bumped by DB trigger on UPDATE

        await unitOfWork.ExecuteSqlAsync(
            $"SELECT set_config('app.current_user_id', {ctx.ActorUserId.ToString()}, true)", ct);

        outbox.Add(EventTypes.DealUpdated, AggregateTypes.Deal, deal.Id,
            deal.OrganizationId, deal.Version + 1, ctx.ActorUserId,
            new { deal_id = deal.Id, version = deal.Version + 1 });

        await unitOfWork.SaveChangesAsync(ct);
        return await ToDtoAsync(deal, false, ct);
    }

    public async Task<DealDto> MoveAsync(Guid id, MoveDealRequest request, CancellationToken ct)
    {
        var deal = await deals.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, deal.OwnerId);

        if (deal.StageId == request.StageId) return await ToDtoAsync(deal, false, ct);

        var stage = await lookup.GetStageAsync(request.StageId, ct)
            ?? throw new SalesValidationException("stageId", "Stage not found.");

        if (stage.PipelineId != deal.PipelineId)
            throw new SalesValidationException("stageId", "Stage does not belong to the deal's pipeline.");

        if (!stage.IsActive)
            throw new SalesValidationException("stageId", "The specified stage is not active.");

        deal.StageId = request.StageId;
        deal.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.ExecuteSqlAsync(
            $"SELECT set_config('app.current_user_id', {ctx.ActorUserId.ToString()}, true)", ct);

        // deal.stage_changed / deal.won / deal.lost written by DB trigger
        await unitOfWork.SaveChangesAsync(ct);
        return await ToDtoAsync(deal, false, ct);
    }

    public async Task<DealDto> MarkWonAsync(Guid id, MarkWonRequest request, CancellationToken ct)
    {
        var deal = await deals.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, deal.OwnerId);

        if (deal.Status == "won") return await ToDtoAsync(deal, false, ct);

        // Find the won stage for this pipeline
        var stages = await lookup.ListStagesAsync(deal.PipelineId, ct);
        var wonStage = stages.FirstOrDefault(s => s.StageType == "won" && s.IsActive)
            ?? throw new SalesValidationException("", "This pipeline has no active 'won' stage.");

        if (request.CloseDate.HasValue)
            deal.ClosedAt = request.CloseDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        deal.StageId = wonStage.Id;
        deal.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.ExecuteSqlAsync(
            $"SELECT set_config('app.current_user_id', {ctx.ActorUserId.ToString()}, true)", ct);

        await unitOfWork.SaveChangesAsync(ct);
        return await ToDtoAsync(deal, false, ct);
    }

    public async Task<DealDto> MarkLostAsync(Guid id, MarkLostRequest request, CancellationToken ct)
    {
        var deal = await deals.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, deal.OwnerId);

        if (deal.Status == "lost") return await ToDtoAsync(deal, false, ct);

        // WL-2: loss reason required
        var lossReason = await lookup.GetLossReasonAsync(request.LossReasonId, ctx.OrganizationId, ct)
            ?? throw new SalesValidationException("lossReasonId", "Loss reason not found.");

        var stages = await lookup.ListStagesAsync(deal.PipelineId, ct);
        var lostStage = stages.FirstOrDefault(s => s.StageType == "lost" && s.IsActive)
            ?? throw new SalesValidationException("", "This pipeline has no active 'lost' stage.");

        deal.LossReasonId = request.LossReasonId;
        deal.LossNotes = request.LossNotes;
        if (request.CloseDate.HasValue)
            deal.ClosedAt = request.CloseDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        deal.StageId = lostStage.Id;
        deal.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.ExecuteSqlAsync(
            $"SELECT set_config('app.current_user_id', {ctx.ActorUserId.ToString()}, true)", ct);

        await unitOfWork.SaveChangesAsync(ct);
        return await ToDtoAsync(deal, false, ct);
    }

    public async Task<DealDto> ReopenAsync(Guid id, ReopenDealRequest request, CancellationToken ct)
    {
        var deal = await deals.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, deal.OwnerId);

        if (deal.Status == "open") return await ToDtoAsync(deal, false, ct);

        var stage = await lookup.GetStageAsync(request.StageId, ct)
            ?? throw new SalesValidationException("stageId", "Stage not found.");

        if (stage.PipelineId != deal.PipelineId)
            throw new SalesValidationException("stageId", "Stage does not belong to the deal's pipeline.");

        if (stage.StageType != "open")
            throw new SalesValidationException("stageId", "Reopen requires an 'open' stage.");

        // WL-3: clear closed fields; DB trigger handles the rest
        deal.ClosedAt = null;
        deal.LossReasonId = null;
        deal.LossNotes = null;
        deal.StageId = request.StageId;
        deal.UpdatedAt = clock.GetUtcNow();

        await unitOfWork.ExecuteSqlAsync(
            $"SELECT set_config('app.current_user_id', {ctx.ActorUserId.ToString()}, true)", ct);

        // deal.reopened is written by DB trigger (V2)
        await unitOfWork.SaveChangesAsync(ct);
        return await ToDtoAsync(deal, false, ct);
    }

    public async Task<DealDto> ReplaceContactsAsync(Guid id, ReplaceDealContactsRequest request, CancellationToken ct)
    {
        var deal = await deals.GetWithDetailsAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, deal.OwnerId);

        // DL-2: at most one primary
        var primaryCount = request.Contacts.Count(c => c.IsPrimary);
        if (primaryCount > 1)
            throw new SalesValidationException("contacts", "At most one contact can be primary (DL-2).");

        // Remove existing contacts
        deal.DealContacts.Clear();

        // Add new contacts
        var now = clock.GetUtcNow();
        foreach (var entry in request.Contacts)
        {
            deal.DealContacts.Add(new DealContact
            {
                DealId = deal.Id,
                ContactId = entry.ContactId,
                Role = entry.Role,
                IsPrimary = entry.IsPrimary,
                CreatedAt = now
            });
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await ToDtoAsync(deal, true, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var deal = await deals.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        PermissionGuard.EnsureCanEdit(ctx, deal.OwnerId);

        var now = clock.GetUtcNow();
        deal.DeletedAt = now;
        deal.UpdatedAt = now;

        outbox.Add(EventTypes.DealDeleted, AggregateTypes.Deal, deal.Id,
            deal.OrganizationId, deal.Version, ctx.ActorUserId,
            new DeletedEventPayload(deal.Id, deal.Version));

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<BulkReassignResult> ReassignAsync(BulkReassignRequest request, CancellationToken ct)
    {
        if (!ctx.IsManagerOrAbove)
            throw new ForbiddenException("Only an admin or manager may bulk-reassign deals.");

        if (request.DealIds.Count > _settings.MaxBulkRecords)
            throw new SalesValidationException($"Cannot reassign more than {_settings.MaxBulkRecords} deals at once.");

        if (request.NewOwnerId.HasValue && !ctx.CanSeeOwner(request.NewOwnerId))
            throw new SalesValidationException("newOwnerId", "You may not assign this owner.");

        var now = clock.GetUtcNow();
        var reassigned = 0;
        var skipped = 0;

        foreach (var dealId in request.DealIds.Distinct())
        {
            var deal = await deals.GetAsync(dealId, ctx, ct);
            if (deal is null) { skipped++; continue; }
            if (deal.Status != "open") { skipped++; continue; }

            var previousOwner = deal.OwnerId;
            deal.OwnerId = request.NewOwnerId;
            deal.UpdatedAt = now;

            outbox.Add(EventTypes.DealReassigned, AggregateTypes.Deal, deal.Id,
                deal.OrganizationId, deal.Version, ctx.ActorUserId,
                new ReassignedEventPayload(deal.Id, previousOwner, deal.OwnerId, deal.Version));

            reassigned++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new BulkReassignResult(reassigned, skipped);
    }

    // ── Mapping helpers ────────────────────────────────────────────────────────

    private async Task<DealDto> ToDtoAsync(Deal deal, bool includeDetails, CancellationToken ct)
    {
        string? pipelineName = null;
        string? stageName = null;
        string? ownerName = null;
        string? companyName = null;
        string? primaryContactName = null;
        string? lossReasonName = null;

        var pipeline = await lookup.GetPipelineAsync(deal.PipelineId, deal.OrganizationId, ct);
        if (pipeline is not null)
        {
            pipelineName = pipeline.Name;
            stageName = pipeline.Stages.FirstOrDefault(s => s.Id == deal.StageId)?.Name;
        }

        if (deal.OwnerId.HasValue)
        {
            var owner = await lookup.GetUserRefAsync(deal.OwnerId.Value, ct);
            ownerName = owner?.DisplayName;
        }

        if (deal.CompanyId.HasValue)
        {
            var companyRef = await lookup.GetCustomerRefAsync("company", deal.CompanyId.Value, ct);
            companyName = companyRef?.DisplayName;
        }

        if (deal.PrimaryContactId.HasValue)
        {
            var contactRef = await lookup.GetCustomerRefAsync("contact", deal.PrimaryContactId.Value, ct);
            primaryContactName = contactRef?.DisplayName;
        }

        if (deal.LossReasonId.HasValue)
        {
            var reason = await lookup.GetLossReasonAsync(deal.LossReasonId.Value, deal.OrganizationId, ct);
            lossReasonName = reason?.Name;
        }

        IReadOnlyList<DealContactDto>? contacts = null;
        IReadOnlyList<StageHistoryDto>? history = null;

        if (includeDetails)
        {
            contacts = await BuildContactDtosAsync(deal.DealContacts, ct);
            history = deal.StageHistory
                .OrderByDescending(h => h.ChangedAt)
                .Select(h => new StageHistoryDto(h.Id, h.FromStageId, null, h.ToStageId, "", h.AmountAtChange, h.ChangedBy, h.ChangedAt))
                .ToList();
        }

        var isStale = IsStale(deal);
        var isOverdue = deal.ExpectedCloseDate.HasValue && deal.Status == "open"
            && deal.ExpectedCloseDate.Value < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        return new DealDto(
            deal.Id, deal.OrganizationId,
            deal.PipelineId, pipelineName,
            deal.StageId, stageName,
            deal.OwnerId, ownerName,
            deal.CompanyId, companyName,
            deal.PrimaryContactId, primaryContactName,
            deal.Name, deal.Amount, deal.Currency,
            deal.Probability, deal.ExpectedCloseDate,
            deal.Status, deal.ClosedAt,
            deal.LossReasonId, lossReasonName, deal.LossNotes,
            deal.SourceLeadId,
            deal.StageEnteredAt, deal.LastActivityAt,
            deal.Tags, ParseCustomFields(deal.CustomFields),
            deal.Version, deal.CreatedBy, deal.CreatedAt, deal.UpdatedAt,
            contacts, history, isStale, isOverdue);
    }

    private async Task<IReadOnlyList<DealContactDto>> BuildContactDtosAsync(
        IReadOnlyList<DealContact> dealContacts, CancellationToken ct)
    {
        var result = new List<DealContactDto>();
        foreach (var dc in dealContacts)
        {
            var contactRef = await lookup.GetCustomerRefAsync("contact", dc.ContactId, ct);
            result.Add(new DealContactDto(dc.ContactId, dc.Role, dc.IsPrimary, contactRef?.DisplayName));
        }
        return result;
    }

    private bool IsStale(Deal deal)
    {
        if (deal.Status != "open") return false;
        var now = clock.GetUtcNow();
        if (deal.LastActivityAt.HasValue &&
            (now - deal.LastActivityAt.Value).TotalDays > _settings.StaleDaysWithoutActivity)
            return true;
        if ((now - deal.StageEnteredAt).TotalDays > _settings.StaleDaysInStage)
            return true;
        return false;
    }

    private static string[] NormalizeTags(IReadOnlyList<string>? tags)
        => tags is null ? [] : [.. tags.Where(t => !string.IsNullOrWhiteSpace(t))
                                       .Select(t => t.Trim().ToLowerInvariant())
                                       .Distinct()];

    private static string SerializeCustomFields(CustomFieldValues? values)
        => values is null || values.Count == 0 ? "{}" : JsonSerializer.Serialize(values);

    private static CustomFieldValues? ParseCustomFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try { return JsonSerializer.Deserialize<CustomFieldValues>(json); }
        catch { return null; }
    }

    private static NotFoundException NotFound(Guid id) => new($"No deal with id {id}.");
}
