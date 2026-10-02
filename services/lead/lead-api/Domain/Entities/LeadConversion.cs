namespace LeadApi.Domain.Entities;

/// <summary>
/// Persisted state for the lead-conversion saga (CLAUDE.md rule 7, CNV-1..CNV-5).
/// The unique constraint on <c>lead_id</c> makes a second conversion attempt a conflict,
/// and the idempotency key on each downstream call uses <c>lead_id</c> so a retried saga step
/// is also safe.
/// </summary>
public class LeadConversion
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>FK to leads (unique — one conversion per lead).</summary>
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }

    public Guid RequestedBy { get; set; }

    /// <summary>JSONB: the full <c>ConversionRequest</c> as submitted.</summary>
    public string Request { get; set; } = "{}";

    /// <summary>started | company_done | contact_done | deal_done | completed | failed | compensating | compensated.</summary>
    public string Status { get; set; } = "started";

    /// <summary>UUID of the company created in Customer service (plain UUID, no FK across services).</summary>
    public Guid? CompanyId { get; set; }

    /// <summary>UUID of the contact created in Customer service (plain UUID, no FK across services).</summary>
    public Guid? ContactId { get; set; }

    /// <summary>UUID of the deal created in Sales service (plain UUID, no FK across services).</summary>
    public Guid? DealId { get; set; }

    public string? LastError { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
