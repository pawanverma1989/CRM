namespace LeadApi.Domain.Entities;

/// <summary>
/// The core lead aggregate (CAP-1..CAP-5, STA-1..STA-4, AC-1..AC-12).
/// A lead must have at least one of <see cref="Email"/> or <see cref="Phone"/> (CAP-1 / AC-2).
/// Converted leads are read-only (STA-4 / AC-11).
/// </summary>
public class Lead
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>The CRM user who owns this lead. Null means unassigned (visible to admin+manager only).</summary>
    public Guid? OwnerId { get; set; }

    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Stored as CITEXT in PostgreSQL for case-insensitive matching.</summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }

    /// <summary>E.164-normalised phone for deduplication and search.</summary>
    public string? PhoneNormalized { get; set; }

    public string? CompanyName { get; set; }
    public string? JobTitle { get; set; }

    /// <summary>new | contacted | qualified | disqualified | converted.</summary>
    public string Status { get; set; } = "new";

    /// <summary>Free-text disqualification reason (legacy); DisqualifyReasonId is the preferred field (STA-3).</summary>
    public string? DisqualifyReason { get; set; }

    /// <summary>FK to disqualify_reasons within this database.</summary>
    public Guid? DisqualifyReasonId { get; set; }
    public DisqualifyReason? DisqualifyReasonNav { get; set; }

    /// <summary>FK to lead_sources within this database.</summary>
    public Guid? LeadSourceId { get; set; }
    public LeadSource? LeadSource { get; set; }

    /// <summary>FK to web_forms (if created via a public form).</summary>
    public Guid? WebFormId { get; set; }
    public WebForm? WebForm { get; set; }

    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? Notes { get; set; }

    /// <summary>Tags stored lower-case, GIN-indexed.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Custom field values as a flat JSON object keyed by field_key.</summary>
    public string CustomFields { get; set; } = "{}";

    /// <summary>Set when status transitions to 'converted' (STA-4).</summary>
    public DateTimeOffset? ConvertedAt { get; set; }
    public Guid? ConvertedBy { get; set; }

    /// <summary>UUID of the contact created in Customer service (plain UUID, no FK across services — CLAUDE.md rule 2).</summary>
    public Guid? ConvertedContactId { get; set; }

    /// <summary>UUID of the company created in Customer service (plain UUID, no FK across services — CLAUDE.md rule 2).</summary>
    public Guid? ConvertedCompanyId { get; set; }

    /// <summary>UUID of the deal created in Sales service (plain UUID, no FK across services — CLAUDE.md rule 2).</summary>
    public Guid? ConvertedDealId { get; set; }

    /// <summary>Optimistic concurrency token — bump on every update (CLAUDE.md).</summary>
    public int Version { get; set; } = 1;

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
