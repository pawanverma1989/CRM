namespace SalesApi.Domain.Entities;

/// <summary>
/// The core deal aggregate (DL-1..DL-6, WL-1..WL-3, BRD-1..BRD-5).
/// status is controlled by the DB trigger (deals_track_stage) from stage_type;
/// application code only moves stage_id.
/// </summary>
public class Deal
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid PipelineId { get; set; }
    public Guid StageId { get; set; }

    /// <summary>The CRM user who owns this deal. Null means unassigned.</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>customer.companies.id — plain UUID, no FK across services (CLAUDE.md rule 2).</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>customer.contacts.id — plain UUID, no FK across services (CLAUDE.md rule 2).</summary>
    public Guid? PrimaryContactId { get; set; }

    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";

    /// <summary>Per-deal probability override; cleared on stage change by V2 trigger (DL-4).</summary>
    public short? Probability { get; set; }

    public DateOnly? ExpectedCloseDate { get; set; }

    /// <summary>open | won | lost — set by DB trigger.</summary>
    public string Status { get; set; } = "open";

    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? LossReasonId { get; set; }
    public string? LossNotes { get; set; }

    /// <summary>lead.leads.id — plain UUID, no FK across services. Unique when not null (DL-6).</summary>
    public Guid? SourceLeadId { get; set; }

    public DateTimeOffset StageEnteredAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string[] Tags { get; set; } = [];
    public string CustomFields { get; set; } = "{}";

    /// <summary>Optimistic concurrency token — bumped on every update by DB trigger.</summary>
    public int Version { get; set; } = 1;

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Navigation
    public Pipeline? Pipeline { get; set; }
    public PipelineStage? Stage { get; set; }
    public LossReason? LossReason { get; set; }
    public List<DealContact> DealContacts { get; set; } = [];
    public List<DealStageHistory> StageHistory { get; set; } = [];
}
