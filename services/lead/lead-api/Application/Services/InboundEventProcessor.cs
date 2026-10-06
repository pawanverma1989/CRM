namespace LeadApi.Application.Services;
using LeadApi.Application.Events;
using LeadApi.Domain.Entities;
using LeadApi.Infrastructure.Data;
using LeadApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Handles consumed events from other services (CLAUDE.md rule 4).
/// Every event is recorded in <c>processed_events</c> first; a redelivery is a no-op.
/// </summary>
public interface IInboundEventProcessor
{
    Task<InboundResult> ProcessAsync(InboundEvent inbound, CancellationToken ct);
    Task<InboundResult> ProcessAsync(string body, string? routingKey, CancellationToken ct);
}

public class InboundEventProcessor(
    LeadDbContext context,
    ILookupRepository lookup,
    IDsrService dsr,
    IOutboxWriter outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<InboundEventProcessor> logger) : IInboundEventProcessor
{
    private static readonly string[] Consumed =
    [
        EventTypes.UserCreated,
        EventTypes.UserUpdated,
        EventTypes.UserDeactivated,
        EventTypes.ActivityLogged,
        EventTypes.ContactMerged,
        EventTypes.CompanyMerged,
        EventTypes.DealDeleted,
        EventTypes.DsrErasureRequested
    ];

    public async Task<InboundResult> ProcessAsync(string body, string? routingKey, CancellationToken ct)
    {
        var inbound = InboundEvent.Parse(body, routingKey);
        if (inbound is null)
        {
            logger.LogWarning("Discarded an inbound message that is not a readable event envelope.");
            return InboundResult.Ignored;
        }

        return await ProcessAsync(inbound, ct);
    }

    public async Task<InboundResult> ProcessAsync(InboundEvent inbound, CancellationToken ct)
    {
        if (!Consumed.Contains(inbound.EventType)) return InboundResult.Ignored;

        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);

        // Claim the event first. Zero rows means it was already handled.
        var claimed = await unitOfWork.ExecuteSqlAsync(
            $"INSERT INTO processed_events (event_id, event_type, processed_at) VALUES ({inbound.EventId}, {inbound.EventType}, {clock.GetUtcNow()}) ON CONFLICT DO NOTHING",
            ct);

        if (claimed == 0)
        {
            logger.LogDebug("Event {EventId} of type {EventType} was already handled.",
                inbound.EventId, inbound.EventType);
            return InboundResult.Duplicate;
        }

        switch (inbound.EventType)
        {
            case EventTypes.UserCreated:
            case EventTypes.UserUpdated:
                await UpsertUserRefAsync(inbound, ct);
                break;

            case EventTypes.UserDeactivated:
                await UpsertUserRefAsync(inbound, ct, forceInactive: true);
                await HandleUserDeactivatedAsync(inbound, ct);
                break;

            case EventTypes.ActivityLogged:
                await HandleActivityLoggedAsync(inbound, ct);
                break;

            case EventTypes.ContactMerged:
                await HandleContactMergedAsync(inbound, ct);
                break;

            case EventTypes.CompanyMerged:
                await HandleCompanyMergedAsync(inbound, ct);
                break;

            case EventTypes.DealDeleted:
                await HandleDealDeletedAsync(inbound, ct);
                break;

            case EventTypes.DsrErasureRequested:
                await HandleDsrErasureAsync(inbound, ct);
                break;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return InboundResult.Handled;
    }

    /// <summary>
    /// user.created / user.updated / user.deactivated: keep the local user_refs copy current
    /// so lists can show owner names without a synchronous Identity call (CLAUDE.md rule 4).
    /// The copy only moves forward; an event without a version is always applied.
    /// </summary>
    private async Task UpsertUserRefAsync(InboundEvent inbound, CancellationToken ct, bool forceInactive = false)
    {
        var userId = InboundEvent.GetGuid(inbound.Payload, "user_id") ?? inbound.AggregateId;
        if (userId == Guid.Empty) return;

        var status = InboundEvent.GetString(inbound.Payload, "status");
        var isActive = !forceInactive && status is null or "active" or "invited";

        // user_refs holds a display name only — never the email (CLAUDE.md: minimise personal data).
        // Identity's first_name is NOT NULL, so the user_id fallback is purely defensive.
        var firstName = InboundEvent.GetString(inbound.Payload, "first_name") ?? string.Empty;
        var lastName = InboundEvent.GetString(inbound.Payload, "last_name") ?? string.Empty;
        var fullName = $"{firstName} {lastName}".Trim();
        var displayName = string.IsNullOrWhiteSpace(fullName) ? userId.ToString() : fullName;

        var existing = await lookup.GetUserRefAsync(userId, ct);

        if (existing is null)
        {
            lookup.AddUserRef(new UserRef
            {
                UserId = userId,
                OrganizationId = inbound.OrganizationId,
                DisplayName = displayName,
                IsActive = isActive,
                SourceVersion = inbound.Version,
                UpdatedAt = clock.GetUtcNow()
            });
            return;
        }

        if (inbound.Version > 0 && inbound.Version <= existing.SourceVersion)
        {
            logger.LogDebug(
                "Ignored out-of-order user event for {UserId}: version {Version} not newer than {Known}.",
                userId, inbound.Version, existing.SourceVersion);
            return;
        }

        existing.DisplayName = displayName;
        existing.IsActive = isActive;
        existing.SourceVersion = inbound.Version;
        existing.UpdatedAt = clock.GetUtcNow();
    }

    /// <summary>
    /// user.deactivated: unassign that user's open leads (set owner_id = null),
    /// publish lead.assigned per lead.
    /// </summary>
    private async Task HandleUserDeactivatedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var userId = InboundEvent.GetGuid(inbound.Payload, "user_id") ?? inbound.AggregateId;
        if (userId == Guid.Empty) return;

        var now = clock.GetUtcNow();

        var affected = await context.Leads
            .Where(l => l.OwnerId == userId && l.DeletedAt == null && l.Status != "converted")
            .ToListAsync(ct);

        foreach (var lead in affected)
        {
            lead.OwnerId = null;
            lead.Version += 1;
            lead.UpdatedAt = now;

            outbox.Add(EventTypes.LeadAssigned, AggregateTypes.Lead, lead.Id,
                lead.OrganizationId, lead.Version, null,
                new AssignedEventPayload(lead.Id, userId, null, lead.Version));
        }

        logger.LogInformation("User {UserId} deactivated: unassigned {Count} lead(s).", userId, affected.Count);
    }

    /// <summary>
    /// activity.logged: if the lead is 'new', move it to 'contacted'.
    /// </summary>
    private async Task HandleActivityLoggedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var leadId = InboundEvent.GetGuid(inbound.Payload, "lead_id");
        if (leadId is null) return;

        var lead = await context.Leads
            .FirstOrDefaultAsync(l => l.Id == leadId && l.OrganizationId == inbound.OrganizationId, ct);

        if (lead is null || lead.Status != "new") return;

        var now = clock.GetUtcNow();
        var previousStatus = lead.Status;
        lead.Status = "contacted";
        lead.Version += 1;
        lead.UpdatedAt = now;

        outbox.Add(EventTypes.LeadStatusChanged, AggregateTypes.Lead, lead.Id,
            lead.OrganizationId, lead.Version, null,
            new StatusChangedPayload(lead.Id, previousStatus, lead.Status, null, lead.Version));

        logger.LogInformation("Lead {LeadId} moved to 'contacted' via activity.logged.", lead.Id);
    }

    /// <summary>
    /// contact.merged: update converted_contact_id from loser to survivor.
    /// </summary>
    private async Task HandleContactMergedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var loserId = InboundEvent.GetGuid(inbound.Payload, "loser_id");
        var survivorId = InboundEvent.GetGuid(inbound.Payload, "survivor_id");

        if (loserId is null || survivorId is null) return;

        var affected = await context.Leads
            .Where(l => l.ConvertedContactId == loserId && l.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var lead in affected)
        {
            lead.ConvertedContactId = survivorId;
            lead.UpdatedAt = clock.GetUtcNow();
        }

        // Also update lead_conversions
        var conversions = await context.LeadConversions
            .Where(c => c.ContactId == loserId && c.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var conv in conversions)
            conv.ContactId = survivorId;

        logger.LogDebug("contact.merged: updated {Count} lead(s) from contact {Loser} to {Survivor}.",
            affected.Count, loserId, survivorId);
    }

    /// <summary>
    /// company.merged: update converted_company_id from loser to survivor.
    /// </summary>
    private async Task HandleCompanyMergedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var loserId = InboundEvent.GetGuid(inbound.Payload, "loser_id");
        var survivorId = InboundEvent.GetGuid(inbound.Payload, "survivor_id");

        if (loserId is null || survivorId is null) return;

        var affected = await context.Leads
            .Where(l => l.ConvertedCompanyId == loserId && l.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var lead in affected)
        {
            lead.ConvertedCompanyId = survivorId;
            lead.UpdatedAt = clock.GetUtcNow();
        }

        var conversions = await context.LeadConversions
            .Where(c => c.CompanyId == loserId && c.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var conv in conversions)
            conv.CompanyId = survivorId;

        logger.LogDebug("company.merged: updated {Count} lead(s) from company {Loser} to {Survivor}.",
            affected.Count, loserId, survivorId);
    }

    /// <summary>
    /// deal.deleted: clear converted_deal_id from affected leads.
    /// </summary>
    private async Task HandleDealDeletedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var dealId = inbound.AggregateId;
        if (dealId == Guid.Empty) return;

        var affected = await context.Leads
            .Where(l => l.ConvertedDealId == dealId && l.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var lead in affected)
        {
            lead.ConvertedDealId = null;
            lead.UpdatedAt = clock.GetUtcNow();
        }

        var conversions = await context.LeadConversions
            .Where(c => c.DealId == dealId && c.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var conv in conversions)
            conv.DealId = null;

        logger.LogDebug("deal.deleted: cleared deal reference from {Count} lead(s).", affected.Count);
    }

    /// <summary>
    /// dsr.erasure_requested: hard-delete all personal data for the subjects.
    /// </summary>
    private async Task HandleDsrErasureAsync(InboundEvent inbound, CancellationToken ct)
    {
        var dsrId = InboundEvent.GetGuid(inbound.Payload, "dsr_id")
                    ?? (inbound.AggregateId == Guid.Empty ? inbound.EventId : inbound.AggregateId);

        var emails = new List<string>();
        var leadIds = new List<Guid>();

        if (inbound.Payload.TryGetProperty("subject_refs", out var refs)
            && refs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in refs.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (Guid.TryParse(text, out var id)) leadIds.Add(id);
                    else if (!string.IsNullOrWhiteSpace(text)) emails.Add(text);
                }
            }
        }

        var requester = InboundEvent.GetString(inbound.Payload, "requester_email");
        if (!string.IsNullOrWhiteSpace(requester)) emails.Add(requester);

        await dsr.EraseAsync(dsrId, inbound.OrganizationId, emails, leadIds, ct);
    }
}
