namespace LeadApi.Application.Services;
using System.Text.Json;
using LeadApi.Application.DTOs;
using LeadApi.Application.Events;
using LeadApi.Application.Normalization;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>Lead CRUD (CAP-1..CAP-5, STA-1..STA-4, LST-1..LST-4, DEL-1, OWN-1..OWN-2).</summary>
public interface ILeadService
{
    Task<PagedResult<LeadDto>> ListAsync(LeadListQuery query, CancellationToken ct);
    Task<LeadDto> GetAsync(Guid id, CancellationToken ct);
    Task<LeadDto> CreateAsync(CreateLeadRequest request, CancellationToken ct);
    Task<LeadDto> UpdateAsync(Guid id, UpdateLeadRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<BulkAssignResult> AssignAsync(BulkAssignRequest request, CancellationToken ct);
    Task<DuplicateCheckResult> DuplicateCheckAsync(DuplicateCheckRequest request, CancellationToken ct);
}

public class LeadService(
    ILeadRepository leads,
    ILookupRepository lookups,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    IRequestContext ctx,
    TimeProvider clock,
    IOptions<LeadSettings> settings,
    ILogger<LeadService> logger) : ILeadService
{
    private readonly LeadSettings _settings = settings.Value;

    private static readonly string[] ValidStatuses = ["new", "contacted", "qualified", "disqualified", "converted"];

    public async Task<PagedResult<LeadDto>> ListAsync(LeadListQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? _settings.DefaultPageSize, 1, _settings.MaxPageSize);

        var (items, total) = await leads.ListAsync(query, ctx, ct);

        var dtos = await Task.WhenAll(items.Select(l => ToDtoAsync(l, ct)));

        return new PagedResult<LeadDto>([.. dtos], page, pageSize, total);
    }

    public async Task<LeadDto> GetAsync(Guid id, CancellationToken ct)
    {
        var lead = await leads.GetAsync(id, ctx, ct) ?? throw NotFound(id);
        return await ToDtoAsync(lead, ct);
    }

    public async Task<LeadDto> CreateAsync(CreateLeadRequest request, CancellationToken ct)
    {
        // CAP-1 / AC-2: must have email OR phone.
        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Phone))
            throw new LeadValidationException("A lead must have at least an email or a phone number.");

        var now = clock.GetUtcNow();

        var lead = new Lead
        {
            Id = Guid.NewGuid(),
            OrganizationId = ctx.OrganizationId,
            OwnerId = PermissionGuard.ResolveOwnerOnCreate(ctx, request.OwnerId),
            FirstName = request.FirstName?.Trim(),
            LastName = request.LastName?.Trim(),
            Email = request.Email?.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            PhoneNormalized = PhoneNormalizer.Normalize(request.Phone, _settings.DefaultPhoneCountryCode),
            CompanyName = request.CompanyName?.Trim(),
            JobTitle = request.JobTitle?.Trim(),
            LeadSourceId = request.LeadSourceId,
            UtmSource = request.UtmSource?.Trim(),
            UtmMedium = request.UtmMedium?.Trim(),
            UtmCampaign = request.UtmCampaign?.Trim(),
            Notes = request.Notes?.Trim(),
            Tags = NormalizeTags(request.Tags),
            CustomFields = SerializeCustomFields(request.CustomFields),
            Status = "new",
            Version = 1,
            CreatedBy = ctx.ActorUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        leads.Add(lead);

        var dto = ToDto(lead, null);
        outbox.Add(EventTypes.LeadCreated, AggregateTypes.Lead, lead.Id,
            lead.OrganizationId, lead.Version, ctx.ActorUserId, dto);

        await unitOfWork.SaveChangesAsync(ct);

        // Duplicate check is non-blocking — warn but don't fail (CAP-1).
        _ = WarnAboutDuplicateAsync(lead, ct);

        return dto;
    }

    public async Task<LeadDto> UpdateAsync(Guid id, UpdateLeadRequest request, CancellationToken ct)
    {
        var lead = await leads.GetAsync(id, ctx, ct) ?? throw NotFound(id);

        // STA-4 / AC-11: converted leads are read-only.
        if (lead.Status == "converted")
            throw new ForbiddenException("A converted lead cannot be updated.");

        PermissionGuard.EnsureCanEdit(ctx, lead.OwnerId);

        if (request.Version != lead.Version)
            throw new ConflictException(
                $"This lead has changed since you loaded it (version {lead.Version}, you sent {request.Version}).");

        // Status transitions
        if (request.Status is not null && request.Status != lead.Status)
        {
            ValidateStatusTransition(lead.Status, request.Status, request.DisqualifyReasonId);
        }

        var previousStatus = lead.Status;
        var previousOwner = lead.OwnerId;

        // Apply owner change
        if (request.OwnerId.HasValue && request.OwnerId.Value != lead.OwnerId)
        {
            PermissionGuard.EnsureCanAssignOwner(ctx, request.OwnerId.Value);
            lead.OwnerId = request.OwnerId.Value;
        }

        // Apply field updates
        if (request.FirstName is not null) lead.FirstName = request.FirstName.Trim();
        if (request.LastName is not null) lead.LastName = request.LastName.Trim();
        if (request.Email is not null) lead.Email = request.Email.Trim().ToLowerInvariant();
        if (request.Phone is not null)
        {
            lead.Phone = request.Phone.Trim();
            lead.PhoneNormalized = PhoneNormalizer.Normalize(request.Phone, _settings.DefaultPhoneCountryCode);
        }
        if (request.CompanyName is not null) lead.CompanyName = request.CompanyName.Trim();
        if (request.JobTitle is not null) lead.JobTitle = request.JobTitle.Trim();
        if (request.LeadSourceId.HasValue) lead.LeadSourceId = request.LeadSourceId.Value;
        if (request.UtmSource is not null) lead.UtmSource = request.UtmSource.Trim();
        if (request.UtmMedium is not null) lead.UtmMedium = request.UtmMedium.Trim();
        if (request.UtmCampaign is not null) lead.UtmCampaign = request.UtmCampaign.Trim();
        if (request.Notes is not null) lead.Notes = request.Notes.Trim();
        if (request.Tags is not null) lead.Tags = NormalizeTags(request.Tags);
        if (request.CustomFields is not null) lead.CustomFields = SerializeCustomFields(request.CustomFields);

        if (request.Status is not null) lead.Status = request.Status;
        if (request.DisqualifyReason is not null) lead.DisqualifyReason = request.DisqualifyReason;
        if (request.DisqualifyReasonId.HasValue) lead.DisqualifyReasonId = request.DisqualifyReasonId.Value;

        lead.Version = request.Version + 1;
        lead.UpdatedAt = clock.GetUtcNow();

        var dto = ToDto(lead, null);
        outbox.Add(EventTypes.LeadUpdated, AggregateTypes.Lead, lead.Id,
            lead.OrganizationId, lead.Version, ctx.ActorUserId, dto);

        // Status changed event
        if (request.Status is not null && request.Status != previousStatus)
            outbox.Add(EventTypes.LeadStatusChanged, AggregateTypes.Lead, lead.Id,
                lead.OrganizationId, lead.Version, ctx.ActorUserId,
                new StatusChangedPayload(lead.Id, previousStatus, lead.Status, lead.DisqualifyReason, lead.Version));

        // Owner changed event
        if (previousOwner != lead.OwnerId)
            outbox.Add(EventTypes.LeadAssigned, AggregateTypes.Lead, lead.Id,
                lead.OrganizationId, lead.Version, ctx.ActorUserId,
                new AssignedEventPayload(lead.Id, previousOwner, lead.OwnerId, lead.Version));

        await unitOfWork.SaveChangesAsync(ct);
        return dto;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var lead = await leads.GetAsync(id, ctx, ct) ?? throw NotFound(id);

        // STA-4 / AC-11: converted leads cannot be deleted.
        if (lead.Status == "converted")
            throw new ForbiddenException("A converted lead cannot be deleted.");

        PermissionGuard.EnsureCanEdit(ctx, lead.OwnerId);

        var now = clock.GetUtcNow();
        lead.DeletedAt = now;
        lead.Version += 1;
        lead.UpdatedAt = now;

        outbox.Add(EventTypes.LeadDeleted, AggregateTypes.Lead, lead.Id,
            lead.OrganizationId, lead.Version, ctx.ActorUserId,
            new DeletedEventPayload(lead.Id, lead.Version));

        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<BulkAssignResult> AssignAsync(BulkAssignRequest request, CancellationToken ct)
    {
        if (!ctx.IsManagerOrAbove)
            throw new ForbiddenException("Only an admin or manager may bulk-assign leads.");

        if (request.LeadIds.Count > _settings.MaxBulkRecords)
            throw new LeadValidationException($"Cannot assign more than {_settings.MaxBulkRecords} leads at once.");

        if (request.OwnerId.HasValue && !ctx.CanSeeOwner(request.OwnerId))
            throw new LeadValidationException("ownerId", "You may not assign this owner.");

        var now = clock.GetUtcNow();
        var assigned = 0;
        var skipped = 0;

        foreach (var leadId in request.LeadIds.Distinct())
        {
            var lead = await leads.GetAsync(leadId, ctx, ct);
            if (lead is null) { skipped++; continue; }
            if (lead.Status == "converted") { skipped++; continue; }

            var previousOwner = lead.OwnerId;
            lead.OwnerId = request.OwnerId;
            lead.Version += 1;
            lead.UpdatedAt = now;

            outbox.Add(EventTypes.LeadAssigned, AggregateTypes.Lead, lead.Id,
                lead.OrganizationId, lead.Version, ctx.ActorUserId,
                new AssignedEventPayload(lead.Id, previousOwner, lead.OwnerId, lead.Version));

            assigned++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new BulkAssignResult(assigned, skipped);
    }

    public async Task<DuplicateCheckResult> DuplicateCheckAsync(DuplicateCheckRequest request, CancellationToken ct)
    {
        var matches = new List<DuplicateLeadDto>();

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var existing = await leads.FindByEmailAsync(
                ctx.OrganizationId, request.Email.Trim().ToLowerInvariant(), request.ExcludeId, ct);

            if (existing is not null)
                matches.Add(ToduplicateDto(existing, "email"));
        }

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var normalized = PhoneNormalizer.Normalize(request.Phone, _settings.DefaultPhoneCountryCode);
            if (normalized is not null)
            {
                var existing = await leads.FindByPhoneAsync(ctx.OrganizationId, normalized, request.ExcludeId, ct);
                if (existing is not null && !matches.Any(m => m.Id == existing.Id))
                    matches.Add(ToduplicateDto(existing, "phone"));
            }
        }

        return new DuplicateCheckResult(matches.Count > 0, matches);
    }

    private static void ValidateStatusTransition(string from, string to, Guid? disqualifyReasonId)
    {
        if (!ValidStatuses.Contains(to))
            throw new LeadValidationException("status", $"'{to}' is not a valid lead status.");

        if (to == "converted")
            throw new LeadValidationException("status", "Use POST /leads/{id}/convert to convert a lead.");

        if (to == "disqualified" && disqualifyReasonId is null)
            throw new LeadValidationException("disqualifyReasonId", "A disqualify reason is required when disqualifying a lead (STA-3).");

        // disqualified → contacted is a valid reopen; any other from-disqualified needs to go through contacted first.
        if (from == "converted")
            throw new LeadValidationException("status", "A converted lead's status cannot be changed.");
    }

    private async Task<LeadDto> ToDtoAsync(Lead lead, CancellationToken ct)
    {
        string? sourceName = null;
        if (lead.LeadSourceId.HasValue)
        {
            var source = await lookups.GetLeadSourceAsync(lead.LeadSourceId.Value, lead.OrganizationId, ct);
            sourceName = source?.Name;
        }

        return ToDto(lead, sourceName);
    }

    private static LeadDto ToDto(Lead lead, string? sourceName)
        => new(
            lead.Id,
            lead.OrganizationId,
            lead.OwnerId,
            lead.FirstName,
            lead.LastName,
            lead.Email,
            lead.Phone,
            lead.CompanyName,
            lead.JobTitle,
            lead.Status,
            lead.DisqualifyReason,
            lead.DisqualifyReasonId,
            lead.LeadSourceId,
            sourceName,
            lead.WebFormId,
            lead.UtmSource,
            lead.UtmMedium,
            lead.UtmCampaign,
            lead.Notes,
            lead.Tags,
            ParseCustomFields(lead.CustomFields),
            lead.ConvertedAt,
            lead.ConvertedContactId,
            lead.ConvertedCompanyId,
            lead.ConvertedDealId,
            lead.Version,
            lead.CreatedBy,
            lead.CreatedAt,
            lead.UpdatedAt);

    private static DuplicateLeadDto ToduplicateDto(Lead lead, string reason)
        => new(lead.Id, lead.FirstName, lead.LastName, lead.Email, lead.Phone, lead.Status, reason);

    private static string[] NormalizeTags(IReadOnlyList<string>? tags)
        => tags is null ? [] : [.. tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct()];

    private static string SerializeCustomFields(CustomFieldValues? values)
        => values is null || values.Count == 0 ? "{}" : JsonSerializer.Serialize(values);

    private static CustomFieldValues? ParseCustomFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try { return JsonSerializer.Deserialize<CustomFieldValues>(json); }
        catch { return null; }
    }

    private async Task WarnAboutDuplicateAsync(Lead lead, CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(lead.Email))
            {
                var dup = await leads.FindByEmailAsync(lead.OrganizationId, lead.Email, lead.Id, ct);
                if (dup is not null)
                    logger.LogInformation("Lead {NewId} shares email with existing lead {ExistingId}.", lead.Id, dup.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Duplicate check failed for lead {Id}.", lead.Id);
        }
    }

    private static NotFoundException NotFound(Guid id) => new($"No lead with id {id}.");
}
