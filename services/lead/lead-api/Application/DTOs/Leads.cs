namespace LeadApi.Application.DTOs;

/// <summary>Full lead representation returned from all write operations and GET /leads/{id}.</summary>
public sealed record LeadDto(
    Guid Id,
    Guid OrganizationId,
    Guid? OwnerId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? CompanyName,
    string? JobTitle,
    string Status,
    string? DisqualifyReason,
    Guid? DisqualifyReasonId,
    Guid? LeadSourceId,
    string? LeadSourceName,
    Guid? WebFormId,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? Notes,
    IReadOnlyList<string> Tags,
    CustomFieldValues? CustomFields,
    DateTimeOffset? ConvertedAt,
    Guid? ConvertedContactId,
    Guid? ConvertedCompanyId,
    Guid? ConvertedDealId,
    int Version,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Recycle-bin representation for admin listing.</summary>
public sealed record RecycleBinLeadDto(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    Guid? OwnerId,
    DateTimeOffset DeletedAt,
    DateTimeOffset PurgeAfter,
    int Version);

/// <summary>POST /leads request body.</summary>
public sealed record CreateLeadRequest
{
    public Guid? OwnerId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? CompanyName { get; init; }
    public string? JobTitle { get; init; }
    public Guid? LeadSourceId { get; init; }
    public string? UtmSource { get; init; }
    public string? UtmMedium { get; init; }
    public string? UtmCampaign { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public CustomFieldValues? CustomFields { get; init; }
}

/// <summary>PATCH /leads/{id} request body. All fields are optional.</summary>
public sealed record UpdateLeadRequest
{
    public Guid? OwnerId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? CompanyName { get; init; }
    public string? JobTitle { get; init; }

    /// <summary>new | contacted | qualified | disqualified. 'converted' is read-only (STA-4).</summary>
    public string? Status { get; init; }

    public string? DisqualifyReason { get; init; }
    public Guid? DisqualifyReasonId { get; init; }
    public Guid? LeadSourceId { get; init; }
    public string? UtmSource { get; init; }
    public string? UtmMedium { get; init; }
    public string? UtmCampaign { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public CustomFieldValues? CustomFields { get; init; }

    /// <summary>Required for optimistic locking (AC-6).</summary>
    public int Version { get; init; }
}

/// <summary>GET /leads query parameters.</summary>
public sealed record LeadListQuery
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? Status { get; init; }
    public Guid? OwnerId { get; init; }
    public Guid? LeadSourceId { get; init; }
    public bool? Unassigned { get; init; }
    public string? UtmCampaign { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public string? SortBy { get; init; }
    public string? SortDir { get; init; }
    public string? Q { get; init; }
}

/// <summary>POST /leads/assign request body.</summary>
public sealed record BulkAssignRequest
{
    public IReadOnlyList<Guid> LeadIds { get; init; } = [];

    /// <summary>Null means unassign (clear owner).</summary>
    public Guid? OwnerId { get; init; }
}

/// <summary>POST /leads/duplicates/check request body.</summary>
public sealed record DuplicateCheckRequest
{
    public Guid? ExcludeId { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
}

/// <summary>A potential duplicate lead found by the duplicate check.</summary>
public sealed record DuplicateLeadDto(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string Status,
    string Reason);

public sealed record DuplicateCheckResult(
    bool HasDuplicates,
    IReadOnlyList<DuplicateLeadDto> Matches);

/// <summary>Bulk assign result.</summary>
public sealed record BulkAssignResult(int Assigned, int Skipped);
