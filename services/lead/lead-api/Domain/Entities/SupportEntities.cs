namespace LeadApi.Domain.Entities;

/// <summary>
/// Transactional outbox row (CLAUDE.md rule 3). <see cref="Id"/> becomes the envelope's
/// <c>event_id</c>; <see cref="Payload"/> holds the complete envelope as JSON so the relay can
/// publish it verbatim.
/// </summary>
public class OutboxEvent
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int PublishAttempts { get; set; }
}

/// <summary>Inbox / idempotency ledger: one row per consumed event (CLAUDE.md rule 4, AC-12).</summary>
public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>
/// Local read-only copy of identity users, for owner names in lead lists.
/// Fed by user.created / user.updated / user.deactivated; an event is applied only when its
/// version beats <see cref="SourceVersion"/> (CLAUDE.md rule 4).
/// </summary>
public class UserRef
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SourceVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
