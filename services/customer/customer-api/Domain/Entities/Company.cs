namespace CustomerApi.Domain.Entities;

/// <summary>
/// A company (account). Mirrors <c>companies</c> in V1 + V2 of the customer_db schema.
/// <c>OwnerId</c>, <c>CreatedBy</c> are identity-service user ids held without a foreign key
/// (CLAUDE.md rule 2).
/// </summary>
public class Company
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? OwnerId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Normalised by <c>DomainNormalizer</c> (COM-4). Stored as <c>citext</c>.</summary>
    public string? Domain { get; set; }

    /// <summary>FK to <c>picklists</c> (list_type = company_industry).</summary>
    public Guid? IndustryId { get; set; }
    public Picklist? Industry { get; set; }

    public int? EmployeeCount { get; set; }
    public decimal? AnnualRevenue { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Gstin { get; set; }

    public string[] Tags { get; set; } = [];

    /// <summary>Raw JSONB object of custom field values, validated against the active definitions (CF-3).</summary>
    public string CustomFields { get; set; } = "{}";

    public Guid? MergedIntoId { get; set; }

    /// <summary>Optimistic lock (COM-3). EF is authoritative; the DB trigger only bumps untouched versions.</summary>
    public int Version { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public List<Contact> Contacts { get; set; } = [];
}
