namespace LeadApi.Application.Events;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The platform event envelope (docs/architecture.md §4.3). The complete envelope is what is
/// stored in <c>outbox_events.payload</c>, so the relay publishes the row verbatim.
/// </summary>
public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    Guid OrganizationId,
    string AggregateType,
    Guid AggregateId,
    int Version,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    object? Payload);

/// <summary>One changed field in a <c>*.updated</c> event.</summary>
public sealed record FieldChange(string Field, object? OldValue, object? NewValue);

/// <summary>Payload shape for *.updated events.</summary>
public sealed record UpdatedEventPayload<T>(T Record, IReadOnlyList<FieldChange> Changes);

/// <summary>Payload shape for *.deleted events.</summary>
public sealed record DeletedEventPayload(Guid Id, int Version);

/// <summary>Payload shape for *.assigned events.</summary>
public sealed record AssignedEventPayload(Guid LeadId, Guid? FromOwnerId, Guid? ToOwnerId, int Version);

/// <summary>Payload shape for lead.status_changed events.</summary>
public sealed record StatusChangedPayload(Guid LeadId, string From, string To, string? Reason, int Version);

/// <summary>Payload shape for lead.converted events.</summary>
public sealed record ConvertedPayload(Guid LeadId, Guid? ContactId, Guid? CompanyId, Guid? DealId, int Version);

/// <summary>Payload shape for web_form.submitted events.</summary>
public sealed record WebFormSubmittedPayload(Guid FormId, Guid LeadId, string? ConsentText, string? IpAddress);

/// <summary>JSON serialization options — snake_case for events.</summary>
public static class LeadJson
{
    public static readonly JsonSerializerOptions Events = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web);

    public static string SerializeEvent<T>(T value) => JsonSerializer.Serialize(value, Events);
}

/// <summary>Event type names this service publishes (§6.1).</summary>
public static class EventTypes
{
    // Published
    public const string LeadCreated = "lead.created";
    public const string LeadUpdated = "lead.updated";
    public const string LeadStatusChanged = "lead.status_changed";
    public const string LeadAssigned = "lead.assigned";
    public const string LeadConverted = "lead.converted";
    public const string LeadDeleted = "lead.deleted";
    public const string LeadRestored = "lead.restored";
    public const string LeadPurged = "lead.purged";
    public const string WebFormSubmitted = "web_form.submitted";
    public const string DsrErasureCompleted = "dsr.erasure_completed";

    // Consumed
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string ActivityLogged = "activity.logged";
    public const string ContactMerged = "contact.merged";
    public const string CompanyMerged = "company.merged";
    public const string DealDeleted = "deal.deleted";
    public const string DsrErasureRequested = "dsr.erasure_requested";
}

public static class AggregateTypes
{
    public const string Lead = "lead";
    public const string WebForm = "web_form";
    public const string Dsr = "dsr";
}

/// <summary>A parsed inbound message.</summary>
public sealed record InboundEvent(
    Guid EventId,
    string EventType,
    Guid OrganizationId,
    Guid AggregateId,
    int Version,
    JsonElement Payload)
{
    public static InboundEvent? Parse(string body, string? routingKey)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(body); }
        catch (JsonException) { return null; }

        if (root.ValueKind != JsonValueKind.Object) return null;

        var eventType = GetString(root, "event_type") ?? routingKey;
        if (string.IsNullOrWhiteSpace(eventType)) return null;

        var payload = root.TryGetProperty("payload", out var inner) ? inner : root;

        return new InboundEvent(
            GetGuid(root, "event_id") ?? Guid.NewGuid(),
            eventType,
            GetGuid(root, "organization_id") ?? GetGuid(payload, "organization_id") ?? Guid.Empty,
            GetGuid(root, "aggregate_id") ?? Guid.Empty,
            GetInt(root, "version") ?? GetInt(payload, "version") ?? 0,
            payload);
    }

    public static string? GetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object
           && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static Guid? GetGuid(JsonElement e, string name)
        => Guid.TryParse(GetString(e, name), out var id) ? id : null;

    public static int? GetInt(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object
           && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt32(out var n) ? n : null;
}

public enum InboundResult { Handled, Duplicate, Ignored }
