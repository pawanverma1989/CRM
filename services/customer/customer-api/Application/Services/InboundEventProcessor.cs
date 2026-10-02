namespace CustomerApi.Application.Services;
using System.Text.Json;
using CustomerApi.Application.Events;
using CustomerApi.Domain.Entities;
using CustomerApi.Infrastructure.Data;
using CustomerApi.Infrastructure.Repositories;

/// <summary>
/// §6.2 consumers, kept out of the broker plumbing so they can be driven directly by a test.
/// Every event is recorded in <c>processed_events</c> with <c>ON CONFLICT DO NOTHING</c> first, so
/// a redelivery is a no-op (CLAUDE.md rule 4, AC-12), and <c>user_refs</c> is only moved forward
/// when the event is newer than the copy it would overwrite.
/// </summary>
public interface IInboundEventProcessor
{
    Task<InboundResult> ProcessAsync(InboundEvent inbound, CancellationToken ct);

    /// <summary>Convenience for the broker consumer and for tests: parse, then process.</summary>
    Task<InboundResult> ProcessAsync(string body, string? routingKey, CancellationToken ct);
}

public enum InboundResult
{
    /// <summary>Applied for the first time.</summary>
    Handled,

    /// <summary>Already in <c>processed_events</c>; nothing was applied.</summary>
    Duplicate,

    /// <summary>Not an event this service consumes, or too old to apply.</summary>
    Ignored
}

/// <summary>A parsed inbound message: the envelope fields this service needs plus the raw payload.</summary>
public sealed record InboundEvent(
    Guid EventId,
    string EventType,
    Guid OrganizationId,
    Guid AggregateId,
    int Version,
    JsonElement Payload)
{
    /// <summary>
    /// Accepts the full envelope of architecture §4.3 and, as a fallback, a bare payload with the
    /// event type taken from the AMQP routing key — which is what a publisher that stores only the
    /// payload in its outbox produces.
    /// </summary>
    public static InboundEvent? Parse(string body, string? routingKey)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(body); }
        catch (JsonException) { return null; }

        if (root.ValueKind != JsonValueKind.Object) return null;

        var eventType = String(root, "event_type") ?? routingKey;
        if (string.IsNullOrWhiteSpace(eventType)) return null;

        var payload = root.TryGetProperty("payload", out var inner) ? inner : root;

        return new InboundEvent(
            Guid(root, "event_id") ?? System.Guid.NewGuid(),
            eventType,
            Guid(root, "organization_id") ?? Guid(payload, "organization_id") ?? System.Guid.Empty,
            Guid(root, "aggregate_id") ?? System.Guid.Empty,
            Int(root, "version") ?? Int(payload, "version") ?? 0,
            payload);
    }

    internal static string? String(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static Guid? Guid(JsonElement element, string name)
        => System.Guid.TryParse(String(element, name), out var id) ? id : null;

    internal static int? Int(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;
}

public class InboundEventProcessor(
    IReassignmentRepository reassignments,
    IDsrService dsr,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<InboundEventProcessor> logger) : IInboundEventProcessor
{
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

        // Architecture §4.2: claim the event first. Zero rows means someone already handled it.
        var claimed = await unitOfWork.ExecuteSqlAsync(
            $"INSERT INTO processed_events (event_id, event_type, processed_at) VALUES ({inbound.EventId}, {inbound.EventType}, {clock.GetUtcNow()}) ON CONFLICT DO NOTHING", ct);

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
                await QueueRecordsOfDeactivatedUserAsync(inbound, ct);
                break;

            case EventTypes.DsrErasureRequested:
                await dsr.EraseAsync(DsrId(inbound), inbound.OrganizationId, Subjects(inbound), ct);
                break;

            case EventTypes.DsrAccessRequested:
                await dsr.ExportAsync(DsrId(inbound), inbound.OrganizationId, Subjects(inbound), ct);
                break;
        }

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return InboundResult.Handled;
    }

    private static readonly string[] Consumed =
    [
        EventTypes.UserCreated,
        EventTypes.UserUpdated,
        EventTypes.UserDeactivated,
        EventTypes.DsrErasureRequested,
        EventTypes.DsrAccessRequested
    ];

    /// <summary>
    /// §6.2: keeps owner names current for list display. The copy only moves forward
    /// (CLAUDE.md rule 4). Identity's user events carry no aggregate version yet, so an event
    /// without one is applied as the latest — the <c>processed_events</c> ledger is what makes a
    /// redelivery harmless in that case.
    /// </summary>
    private async Task UpsertUserRefAsync(InboundEvent inbound, CancellationToken ct, bool forceInactive = false)
    {
        var payload = inbound.Payload;
        var userId = InboundEvent.Guid(payload, "user_id") ?? inbound.AggregateId;
        if (userId == Guid.Empty) return;

        var status = InboundEvent.String(payload, "status");
        var isActive = !forceInactive && status is null or "active" or "invited";

        var displayName = DisplayName(payload) ?? userId.ToString();
        var existing = await reassignments.UserRefAsync(userId, ct);

        if (existing is null)
        {
            reassignments.AddUserRef(new UserRef
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
                "Ignored an out-of-order user event for {UserId}: version {Version} is not newer than {Known}.",
                userId, inbound.Version, existing.SourceVersion);
            return;
        }

        existing.DisplayName = displayName;
        existing.IsActive = isActive;
        existing.OrganizationId = inbound.OrganizationId == Guid.Empty
            ? existing.OrganizationId
            : inbound.OrganizationId;
        if (inbound.Version > 0) existing.SourceVersion = inbound.Version;
        existing.UpdatedAt = clock.GetUtcNow();
    }

    /// <summary>
    /// OWN-3 / AC-12: the deactivated user's live records join the queue. The insert relies on
    /// <c>uq_reassignment_open</c>, so a second delivery adds nothing even if the ledger were lost.
    /// </summary>
    private async Task QueueRecordsOfDeactivatedUserAsync(InboundEvent inbound, CancellationToken ct)
    {
        var userId = InboundEvent.Guid(inbound.Payload, "user_id") ?? inbound.AggregateId;
        if (userId == Guid.Empty) return;

        var now = clock.GetUtcNow();

        var contactsQueued = await unitOfWork.ExecuteSqlAsync(
            $"""
             INSERT INTO reassignment_queue (organization_id, record_type, record_id, previous_owner_id, queued_at)
             SELECT organization_id, 'contact', id, owner_id, {now}
             FROM   contacts
             WHERE  owner_id = {userId} AND deleted_at IS NULL
             ON CONFLICT DO NOTHING
             """, ct);

        var companiesQueued = await unitOfWork.ExecuteSqlAsync(
            $"""
             INSERT INTO reassignment_queue (organization_id, record_type, record_id, previous_owner_id, queued_at)
             SELECT organization_id, 'company', id, owner_id, {now}
             FROM   companies
             WHERE  owner_id = {userId} AND deleted_at IS NULL
             ON CONFLICT DO NOTHING
             """, ct);

        logger.LogInformation(
            "User {UserId} was deactivated: queued {Contacts} contact(s) and {Companies} company/companies for reassignment.",
            userId, contactsQueued, companiesQueued);
    }

    private static Guid DsrId(InboundEvent inbound)
        => InboundEvent.Guid(inbound.Payload, "dsr_id")
           ?? (inbound.AggregateId == Guid.Empty ? inbound.EventId : inbound.AggregateId);

    /// <summary>
    /// Reads the subjects out of a compliance request. <c>subject_refs</c> is the authoritative
    /// list; <c>requester_email</c> is also treated as a subject because a data subject request is
    /// normally made by the person it concerns.
    /// </summary>
    private static DsrSubjects Subjects(InboundEvent inbound)
    {
        var payload = inbound.Payload;
        var ids = new List<Guid>();
        var emails = new List<string>();

        if (payload.TryGetProperty("subject_refs", out var refs) && refs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in refs.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (Guid.TryParse(text, out var id)) ids.Add(id);
                    else if (!string.IsNullOrWhiteSpace(text)) emails.Add(text);
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object) continue;

                var entity = InboundEvent.String(item, "entity_type") ?? InboundEvent.String(item, "type");
                if (entity is not null && entity != EntityTypes.Contact) continue;

                var recordId = InboundEvent.Guid(item, "id")
                               ?? InboundEvent.Guid(item, "record_id")
                               ?? InboundEvent.Guid(item, "contact_id");
                if (recordId is not null) ids.Add(recordId.Value);

                var email = InboundEvent.String(item, "email");
                if (!string.IsNullOrWhiteSpace(email)) emails.Add(email);
            }
        }

        AddAll(payload, "contact_ids", value => { if (Guid.TryParse(value, out var id)) ids.Add(id); });
        AddAll(payload, "subject_emails", emails.Add);
        AddAll(payload, "emails", emails.Add);

        var requester = InboundEvent.String(payload, "requester_email");
        if (!string.IsNullOrWhiteSpace(requester)) emails.Add(requester);

        return new DsrSubjects(ids, emails);
    }

    private static void AddAll(JsonElement payload, string name, Action<string> add)
    {
        if (!payload.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return;

        foreach (var item in array.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                add(value);
    }

    private static string? DisplayName(JsonElement payload)
    {
        var first = InboundEvent.String(payload, "first_name");
        var last = InboundEvent.String(payload, "last_name");
        var combined = $"{first} {last}".Trim();

        if (combined.Length > 0) return combined;

        var displayName = InboundEvent.String(payload, "display_name");
        if (!string.IsNullOrWhiteSpace(displayName)) return displayName;

        return InboundEvent.String(payload, "email");
    }
}
