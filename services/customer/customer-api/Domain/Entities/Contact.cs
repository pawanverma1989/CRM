namespace CustomerApi.Domain.Entities;

/// <summary>
/// A person. Mirrors <c>contacts</c> in V1 + V2 of the customer_db schema.
/// <c>SourceLeadId</c> is a lead-service id held without a foreign key and is unique,
/// so a retried lead conversion never creates a second contact (CON-7).
/// </summary>
public class Contact
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>Real FK inside this database; <c>ON DELETE SET NULL</c> (COM-5).</summary>
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }

    public Guid? OwnerId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }

    /// <summary>Stored as entered, compared case-insensitively (<c>citext</c>, CON-3).</summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }

    /// <summary>E.164 form of <see cref="Phone"/> (CON-4).</summary>
    public string? PhoneNormalized { get; set; }

    public string? Mobile { get; set; }
    public string? JobTitle { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    /// <summary>FK to <c>picklists</c> (list_type = contact_source).</summary>
    public Guid? SourceId { get; set; }
    public Picklist? Source { get; set; }

    public Guid? SourceLeadId { get; set; }

    public string[] Tags { get; set; } = [];
    public string CustomFields { get; set; } = "{}";

    public Guid? MergedIntoId { get; set; }

    /// <summary>Optimistic lock (CON-6, AC-6).</summary>
    public int Version { get; set; }

    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
