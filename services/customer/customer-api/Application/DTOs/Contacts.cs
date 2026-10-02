namespace CustomerApi.Application.DTOs;
using System.Text.Json;
using CustomerApi.Application.Json;

public sealed record ContactDto(
    Guid Id,
    Guid OrganizationId,
    Guid? CompanyId,
    string? CompanyName,
    Guid? OwnerId,
    string? OwnerName,
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? PhoneNormalized,
    string? Mobile,
    string? JobTitle,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    Guid? SourceId,
    string? Source,
    Guid? SourceLeadId,
    IReadOnlyList<string> Tags,
    JsonElement CustomFields,
    Guid? MergedIntoId,
    int Version,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

public sealed record CreateContactRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Mobile { get; init; }
    public string? JobTitle { get; init; }
    public Guid? CompanyId { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public Guid? SourceId { get; init; }
    public Guid? OwnerId { get; init; }

    /// <summary>
    /// CON-7: set by the lead service when converting a lead. A second request with the same
    /// value returns the existing contact instead of creating another (AC-16).
    /// </summary>
    public Guid? SourceLeadId { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }
    public CustomFieldValues? CustomFields { get; init; }
}

/// <summary>PATCH body; absent properties are left alone, an explicit <c>null</c> clears (CON-6).</summary>
public sealed record UpdateContactRequest
{
    public int Version { get; init; }

    public Optional<string> FirstName { get; init; }
    public Optional<string?> LastName { get; init; }
    public Optional<string?> Email { get; init; }
    public Optional<string?> Phone { get; init; }
    public Optional<string?> Mobile { get; init; }
    public Optional<string?> JobTitle { get; init; }
    public Optional<Guid?> CompanyId { get; init; }
    public Optional<string?> AddressLine1 { get; init; }
    public Optional<string?> AddressLine2 { get; init; }
    public Optional<string?> City { get; init; }
    public Optional<string?> State { get; init; }
    public Optional<string?> PostalCode { get; init; }
    public Optional<string?> Country { get; init; }
    public Optional<Guid?> SourceId { get; init; }
    public Optional<Guid?> OwnerId { get; init; }
    public Optional<IReadOnlyList<string>> Tags { get; init; }
    public Optional<CustomFieldValues> CustomFields { get; init; }
}

public sealed record ContactListQuery : ListQueryBase
{
    public Guid? CompanyId { get; init; }
    public Guid? SourceId { get; init; }
}

/// <summary>One row of a bulk import call (§5, AC-18).</summary>
public sealed record BulkUpsertRequest
{
    /// <summary>contact | company.</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>skip | update | create — what to do when a row matches a live record (DUP-1).</summary>
    public string DuplicateStrategy { get; init; } = "skip";

    public IReadOnlyList<BulkUpsertRow> Rows { get; init; } = [];
}

public sealed record BulkUpsertRow
{
    public string? Name { get; init; }
    public string? Domain { get; init; }
    public Guid? IndustryId { get; init; }
    public int? EmployeeCount { get; init; }
    public decimal? AnnualRevenue { get; init; }
    public string? Website { get; init; }
    public string? Gstin { get; init; }

    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Mobile { get; init; }
    public string? JobTitle { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? SourceId { get; init; }

    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }

    public Guid? OwnerId { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public CustomFieldValues? CustomFields { get; init; }
}
