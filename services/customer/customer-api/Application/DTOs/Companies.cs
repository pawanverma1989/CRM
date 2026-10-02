namespace CustomerApi.Application.DTOs;
using System.Text.Json;
using CustomerApi.Application.Json;

/// <summary>
/// A company as the API returns it. <c>Industry</c> is the label resolved from
/// <c>picklists</c>, alongside the raw <c>IndustryId</c>.
/// </summary>
public sealed record CompanyDto(
    Guid Id,
    Guid OrganizationId,
    Guid? OwnerId,
    string? OwnerName,
    string Name,
    string? Domain,
    Guid? IndustryId,
    string? Industry,
    int? EmployeeCount,
    decimal? AnnualRevenue,
    string AnnualRevenueCurrency,
    string? Phone,
    string? Website,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Gstin,
    IReadOnlyList<string> Tags,
    JsonElement CustomFields,
    Guid? MergedIntoId,
    int Version,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

/// <summary>COM-2: the detail view adds the company's contacts.</summary>
public sealed record CompanyDetailDto(CompanyDto Company, IReadOnlyList<ContactSummaryDto> Contacts);

public sealed record ContactSummaryDto(
    Guid Id,
    string FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    Guid? OwnerId,
    string? OwnerName);

public sealed record CreateCompanyRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Domain { get; init; }
    public Guid? IndustryId { get; init; }
    public int? EmployeeCount { get; init; }
    public decimal? AnnualRevenue { get; init; }
    public string? Phone { get; init; }
    public string? Website { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
    public string? Gstin { get; init; }

    /// <summary>Admins and managers may name any owner they can see; a sales rep becomes the owner (OWN-1).</summary>
    public Guid? OwnerId { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }
    public CustomFieldValues? CustomFields { get; init; }
}

/// <summary>
/// PATCH body. Absent properties are left alone; an explicit <c>null</c> clears the field.
/// <see cref="Version"/> is mandatory — a stale version is a 409 (COM-3, AC-6).
/// </summary>
public sealed record UpdateCompanyRequest
{
    public int Version { get; init; }

    public Optional<string> Name { get; init; }
    public Optional<string?> Domain { get; init; }
    public Optional<Guid?> IndustryId { get; init; }
    public Optional<int?> EmployeeCount { get; init; }
    public Optional<decimal?> AnnualRevenue { get; init; }
    public Optional<string?> Phone { get; init; }
    public Optional<string?> Website { get; init; }
    public Optional<string?> AddressLine1 { get; init; }
    public Optional<string?> AddressLine2 { get; init; }
    public Optional<string?> City { get; init; }
    public Optional<string?> State { get; init; }
    public Optional<string?> PostalCode { get; init; }
    public Optional<string?> Country { get; init; }
    public Optional<string?> Gstin { get; init; }
    public Optional<Guid?> OwnerId { get; init; }
    public Optional<IReadOnlyList<string>> Tags { get; init; }
    public Optional<CustomFieldValues> CustomFields { get; init; }
}

/// <summary>LST-1..LST-4 query parameters for <c>GET /companies</c>.</summary>
public sealed record CompanyListQuery : ListQueryBase
{
    public Guid? IndustryId { get; init; }
}

/// <summary>Filters shared by both list endpoints (LST-2, LST-3).</summary>
public abstract record ListQueryBase
{
    public int Page { get; init; } = 1;
    public int? PageSize { get; init; }

    /// <summary>LST-4: matches name, email, phone and domain, partial and fuzzy.</summary>
    public string? Q { get; init; }

    public Guid? OwnerId { get; init; }

    /// <summary>Repeatable; a record must carry every tag given.</summary>
    public string[]? Tags { get; init; }

    public string? City { get; init; }
    public string? State { get; init; }
    public string? Country { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public DateTimeOffset? UpdatedFrom { get; init; }
    public DateTimeOffset? UpdatedTo { get; init; }

    /// <summary>JSONB containment filter, e.g. <c>?customField=region:north</c>.</summary>
    public string[]? CustomField { get; init; }

    /// <summary>name | created | updated | owner.</summary>
    public string? Sort { get; init; }

    /// <summary>asc | desc.</summary>
    public string? Direction { get; init; }
}
