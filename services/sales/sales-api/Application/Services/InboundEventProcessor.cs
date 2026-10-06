namespace SalesApi.Application.Services;
using SalesApi.Application.Events;
using SalesApi.Domain.Entities;
using SalesApi.Infrastructure.Data;
using SalesApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Handles consumed events from other services (CLAUDE.md rule 4).
/// Every event is recorded in <c>processed_events</c> first; a redelivery is a no-op.
///
/// Events handled:
///   company.*          → upsert customer_refs
///   contact.*          → upsert customer_refs
///   company.merged     → re-point deals to survivor_id
///   contact.merged     → re-point deal_contacts to survivor_id
///   activity.logged    → update deals.last_activity_at
///   user.created/updated → upsert user_refs
///   user.deactivated   → add open deals to reassignment_queue; upsert user_refs
///   dsr.erasure_requested → hard-delete personal data, publish dsr.erasure_completed
/// </summary>
public interface IInboundEventProcessor
{
    Task<InboundResult> ProcessAsync(InboundEvent inbound, CancellationToken ct);
    Task<InboundResult> ProcessAsync(string body, string? routingKey, CancellationToken ct);
}

public class InboundEventProcessor(
    SalesDbContext context,
    ILookupRepository lookup,
    IDsrService dsr,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<InboundEventProcessor> logger) : IInboundEventProcessor
{
    private static readonly string[] Consumed =
    [
        EventTypes.CompanyCreated, EventTypes.CompanyUpdated, EventTypes.CompanyDeleted, EventTypes.CompanyMerged,
        EventTypes.ContactCreated, EventTypes.ContactUpdated, EventTypes.ContactDeleted, EventTypes.ContactMerged,
        EventTypes.ActivityLogged,
        EventTypes.UserCreated, EventTypes.UserUpdated, EventTypes.UserDeactivated,
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

        // Claim the event first — zero rows means already handled
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
            case EventTypes.CompanyCreated:
            case EventTypes.CompanyUpdated:
                await UpsertCustomerRefAsync("company", inbound, ct);
                break;

            case EventTypes.CompanyDeleted:
                await UpsertCustomerRefAsync("company", inbound, ct, forceDeleted: true);
                break;

            case EventTypes.CompanyMerged:
                await UpsertCustomerRefAsync("company", inbound, ct);
                await HandleCompanyMergedAsync(inbound, ct);
                break;

            case EventTypes.ContactCreated:
            case EventTypes.ContactUpdated:
                await UpsertCustomerRefAsync("contact", inbound, ct);
                break;

            case EventTypes.ContactDeleted:
                await UpsertCustomerRefAsync("contact", inbound, ct, forceDeleted: true);
                break;

            case EventTypes.ContactMerged:
                await UpsertCustomerRefAsync("contact", inbound, ct);
                await HandleContactMergedAsync(inbound, ct);
                break;

            case EventTypes.ActivityLogged:
                await HandleActivityLoggedAsync(inbound, ct);
                break;

            case EventTypes.UserCreated:
            case EventTypes.UserUpdated:
                await UpsertUserRefAsync(inbound, ct);
                break;

            case EventTypes.UserDeactivated:
                await UpsertUserRefAsync(inbound, ct, forceInactive: true);
                await HandleUserDeactivatedAsync(inbound, ct);
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
    /// company.* / contact.*: keep the local customer_refs copy current (CLAUDE.md rule 4, NFR-4).
    /// The copy only moves forward.
    /// </summary>
    private async Task UpsertCustomerRefAsync(
        string entityType, InboundEvent inbound, CancellationToken ct, bool forceDeleted = false)
    {
        var id = inbound.AggregateId;
        if (id == Guid.Empty) return;

        var displayName = InboundEvent.GetString(inbound.Payload, "name")
            ?? InboundEvent.GetString(inbound.Payload, "display_name")
            ?? (entityType == "contact"
                ? BuildContactName(inbound)
                : null)
            ?? id.ToString();

        var email = InboundEvent.GetString(inbound.Payload, "email");

        var existing = await lookup.GetCustomerRefAsync(entityType, id, ct);
        if (existing is not null && inbound.Version > 0 && inbound.Version <= existing.SourceVersion)
        {
            logger.LogDebug("Ignored out-of-order {Type} event for {Id}: version {V} not newer than {Known}.",
                entityType, id, inbound.Version, existing.SourceVersion);
            return;
        }

        lookup.UpsertCustomerRef(new CustomerRef
        {
            EntityType = entityType,
            Id = id,
            OrganizationId = inbound.OrganizationId,
            DisplayName = displayName,
            Email = email,
            IsDeleted = forceDeleted,
            SourceVersion = inbound.Version,
            UpdatedAt = clock.GetUtcNow()
        });
    }

    private static string BuildContactName(InboundEvent inbound)
    {
        var firstName = InboundEvent.GetString(inbound.Payload, "first_name") ?? string.Empty;
        var lastName = InboundEvent.GetString(inbound.Payload, "last_name") ?? string.Empty;
        var name = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? inbound.AggregateId.ToString() : name;
    }

    /// <summary>
    /// company.merged: re-point deals.company_id from loser to survivor.
    /// </summary>
    private async Task HandleCompanyMergedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var loserId = InboundEvent.GetGuid(inbound.Payload, "loser_id");
        var survivorId = InboundEvent.GetGuid(inbound.Payload, "survivor_id");
        if (loserId is null || survivorId is null) return;

        var affected = await context.Deals
            .Where(d => d.CompanyId == loserId && d.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var deal in affected)
        {
            deal.CompanyId = survivorId;
            deal.UpdatedAt = clock.GetUtcNow();
        }

        logger.LogDebug("company.merged: updated {Count} deal(s) from company {Loser} to {Survivor}.",
            affected.Count, loserId, survivorId);
    }

    /// <summary>
    /// contact.merged: re-point deal_contacts.contact_id and deals.primary_contact_id from loser to survivor.
    /// </summary>
    private async Task HandleContactMergedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var loserId = InboundEvent.GetGuid(inbound.Payload, "loser_id");
        var survivorId = InboundEvent.GetGuid(inbound.Payload, "survivor_id");
        if (loserId is null || survivorId is null) return;

        // Update deal_contacts
        var dealContacts = await context.DealContacts
            .Where(dc => dc.ContactId == loserId)
            .ToListAsync(ct);

        // Avoid duplicates: if (dealId, survivorId) already exists, remove the loser row
        foreach (var dc in dealContacts)
        {
            var survivorExists = await context.DealContacts
                .AnyAsync(x => x.DealId == dc.DealId && x.ContactId == survivorId.Value, ct);

            if (survivorExists)
                context.DealContacts.Remove(dc);
            else
                dc.ContactId = survivorId.Value;
        }

        // Update primary_contact_id
        var primaryDeals = await context.Deals
            .Where(d => d.PrimaryContactId == loserId && d.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var deal in primaryDeals)
        {
            deal.PrimaryContactId = survivorId;
            deal.UpdatedAt = clock.GetUtcNow();
        }

        logger.LogDebug("contact.merged: updated {Count} deal contact(s) from contact {Loser} to {Survivor}.",
            dealContacts.Count, loserId, survivorId);
    }

    /// <summary>
    /// activity.logged: update deals.last_activity_at for the referenced deal.
    /// </summary>
    private async Task HandleActivityLoggedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var dealId = InboundEvent.GetGuid(inbound.Payload, "deal_id");
        if (dealId is null) return;

        var deal = await context.Deals
            .FirstOrDefaultAsync(d => d.Id == dealId && d.OrganizationId == inbound.OrganizationId, ct);

        if (deal is null || deal.Status != "open") return;

        var now = clock.GetUtcNow();
        deal.LastActivityAt = now;
        deal.UpdatedAt = now;

        logger.LogDebug("Deal {DealId} last_activity_at updated via activity.logged.", dealId);
    }

    /// <summary>
    /// user.created / user.updated / user.deactivated: keep the local user_refs copy current.
    /// </summary>
    private async Task UpsertUserRefAsync(InboundEvent inbound, CancellationToken ct, bool forceInactive = false)
    {
        var userId = InboundEvent.GetGuid(inbound.Payload, "user_id") ?? inbound.AggregateId;
        if (userId == Guid.Empty) return;

        var status = InboundEvent.GetString(inbound.Payload, "status");
        var isActive = !forceInactive && status is null or "active" or "invited";

        var firstName = InboundEvent.GetString(inbound.Payload, "first_name") ?? string.Empty;
        var lastName = InboundEvent.GetString(inbound.Payload, "last_name") ?? string.Empty;
        var email = InboundEvent.GetString(inbound.Payload, "email") ?? string.Empty;
        var displayName = string.IsNullOrWhiteSpace($"{firstName} {lastName}".Trim())
            ? (string.IsNullOrWhiteSpace(email) ? userId.ToString() : email)
            : $"{firstName} {lastName}".Trim();

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
            logger.LogDebug("Ignored out-of-order user event for {UserId}: version {V} not newer than {Known}.",
                userId, inbound.Version, existing.SourceVersion);
            return;
        }

        existing.DisplayName = displayName;
        existing.IsActive = isActive;
        existing.SourceVersion = inbound.Version;
        existing.UpdatedAt = clock.GetUtcNow();
    }

    /// <summary>
    /// user.deactivated: add all that user's open deals to reassignment_queue (OWN-2).
    /// </summary>
    private async Task HandleUserDeactivatedAsync(InboundEvent inbound, CancellationToken ct)
    {
        var userId = InboundEvent.GetGuid(inbound.Payload, "user_id") ?? inbound.AggregateId;
        if (userId == Guid.Empty) return;

        var now = clock.GetUtcNow();

        var openDeals = await context.Deals
            .Where(d => d.OwnerId == userId
                     && d.Status == "open"
                     && d.DeletedAt == null
                     && d.OrganizationId == inbound.OrganizationId)
            .ToListAsync(ct);

        foreach (var deal in openDeals)
        {
            // Check if already queued
            var alreadyQueued = await context.ReassignmentQueue
                .AnyAsync(r => r.DealId == deal.Id, ct);

            if (alreadyQueued) continue;

            context.ReassignmentQueue.Add(new ReassignmentQueue
            {
                Id = Guid.NewGuid(),
                OrganizationId = inbound.OrganizationId,
                DealId = deal.Id,
                DeactivatedUserId = userId,
                QueuedAt = now
            });
        }

        logger.LogInformation("User {UserId} deactivated: {Count} open deal(s) added to reassignment queue.",
            userId, openDeals.Count);
    }

    /// <summary>
    /// dsr.erasure_requested: hard-delete personal data for the contact subjects.
    /// </summary>
    private async Task HandleDsrErasureAsync(InboundEvent inbound, CancellationToken ct)
    {
        var dsrId = InboundEvent.GetGuid(inbound.Payload, "dsr_id")
                    ?? (inbound.AggregateId == Guid.Empty ? inbound.EventId : inbound.AggregateId);

        var contactIds = new List<Guid>();

        if (inbound.Payload.TryGetProperty("subject_refs", out var refs)
            && refs.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in refs.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (Guid.TryParse(text, out var id))
                        contactIds.Add(id);
                }
            }
        }

        await dsr.EraseAsync(dsrId, inbound.OrganizationId, contactIds, ct);
    }
}
