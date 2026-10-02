namespace CustomerApi.Application.Events;

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

/// <summary>One changed field in a <c>*.updated</c> event — the compliance audit log needs old and new values.</summary>
public sealed record FieldChange(string Field, object? OldValue, object? NewValue);

/// <summary>Event type names this service publishes (§6.1).</summary>
public static class EventTypes
{
    public const string CompanyCreated = "company.created";
    public const string CompanyUpdated = "company.updated";
    public const string CompanyDeleted = "company.deleted";
    public const string CompanyRestored = "company.restored";
    public const string CompanyMerged = "company.merged";
    public const string CompanyReassigned = "company.reassigned";
    public const string CompanyPurged = "company.purged";

    public const string ContactCreated = "contact.created";
    public const string ContactUpdated = "contact.updated";
    public const string ContactDeleted = "contact.deleted";
    public const string ContactRestored = "contact.restored";
    public const string ContactMerged = "contact.merged";
    public const string ContactReassigned = "contact.reassigned";
    public const string ContactPurged = "contact.purged";

    public const string DsrErasureCompleted = "dsr.erasure_completed";
    public const string DsrAccessCompleted = "dsr.access_completed";

    // Consumed (§6.2)
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserDeactivated = "user.deactivated";
    public const string DsrErasureRequested = "dsr.erasure_requested";
    public const string DsrAccessRequested = "dsr.access_requested";
}

public static class AggregateTypes
{
    public const string Company = "company";
    public const string Contact = "contact";
    public const string Dsr = "dsr";
}
