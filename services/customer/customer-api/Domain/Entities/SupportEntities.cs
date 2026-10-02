namespace CustomerApi.Domain.Entities;

/// <summary>Admin-defined custom field (CF-1). <c>FieldKey</c> and <c>FieldType</c> are immutable (CF-6).</summary>
public class CustomFieldDefinition
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>contact | company.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Matches <c>^[a-z][a-z0-9_]*$</c>.</summary>
    public string FieldKey { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>text | textarea | number | currency | date | datetime | boolean | select | multiselect | email | phone | url | user (CF-2).</summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>JSONB array of allowed values for select / multiselect.</summary>
    public string? Options { get; set; }

    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Deactivating hides the field but keeps stored values (CF-5).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One row per completed merge (DUP-5). Also used to find merged copies during erasure (DSR-1).</summary>
public class MergeHistory
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid SurvivorId { get; set; }
    public Guid LoserId { get; set; }

    /// <summary>JSONB map of field name to the record the kept value came from.</summary>
    public string? FieldChoices { get; set; }

    public Guid? MergedBy { get; set; }
    public DateTimeOffset MergedAt { get; set; }
}

/// <summary>Admin-managed value list: company industries and contact sources (§4).</summary>
public class Picklist
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>company_industry | contact_source.</summary>
    public string ListType { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Records owned by a deactivated user, waiting for an admin to pick a new owner (OWN-3).
/// <c>uq_reassignment_open</c> makes a redelivered <c>user.deactivated</c> a no-op (AC-12).
/// </summary>
public class ReassignmentQueueEntry
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>contact | company.</summary>
    public string RecordType { get; set; } = string.Empty;

    public Guid RecordId { get; set; }
    public Guid PreviousOwnerId { get; set; }
    public Guid? NewOwnerId { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
}

/// <summary>
/// Local read-only copy of identity users, so lists can show owner names without a synchronous
/// call. An event is applied only when its version beats <see cref="SourceVersion"/> (CLAUDE.md rule 4).
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
