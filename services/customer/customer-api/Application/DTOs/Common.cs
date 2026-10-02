namespace CustomerApi.Application.DTOs;
using System.Text.Json;

/// <summary>List response shape used by every list endpoint (LST-1).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Data, int Page, int PageSize, int Total);

/// <summary>Owner name for list display, served from the local <c>user_refs</c> copy.</summary>
public sealed record OwnerDto(Guid Id, string DisplayName, bool IsActive);

/// <summary>A value from an admin-managed list (§4).</summary>
public sealed record PicklistDto(Guid Id, string ListType, string Value, int SortOrder, bool IsActive);

public sealed record CreatePicklistValueRequest
{
    /// <summary>company_industry | contact_source.</summary>
    public string ListType { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public int? SortOrder { get; init; }
}

public sealed record UpdatePicklistValueRequest
{
    public string? Value { get; init; }
    public int? SortOrder { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>Custom field definition for form rendering (CF-1).</summary>
public sealed record CustomFieldDefinitionDto(
    Guid Id,
    string EntityType,
    string FieldKey,
    string Label,
    string FieldType,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    int SortOrder,
    bool IsActive);

public sealed record CreateCustomFieldRequest
{
    public string EntityType { get; init; } = string.Empty;
    public string FieldKey { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string FieldType { get; init; } = string.Empty;
    public IReadOnlyList<string>? Options { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>CF-6: only label, options, order and the active flag may change.</summary>
public sealed record UpdateCustomFieldRequest
{
    public string? Label { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public bool? IsRequired { get; init; }
    public int? SortOrder { get; init; }
    public bool? IsActive { get; init; }
}

/// <summary>POST /duplicates/check — soft warnings for a draft record (DUP-2, DUP-3).</summary>
public sealed record DuplicateCheckRequest
{
    /// <summary>contact | company.</summary>
    public string EntityType { get; init; } = "contact";

    /// <summary>Excluded from the results, so editing a record never flags itself.</summary>
    public Guid? ExcludeId { get; init; }

    public string? Name { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Phone { get; init; }
    public string? Domain { get; init; }
    public Guid? CompanyId { get; init; }
}

/// <summary>A hard duplicate blocks the save (DUP-1); a soft one is only a warning.</summary>
public sealed record DuplicateMatchDto(
    Guid Id,
    string Name,
    string Reason,
    bool Blocking,
    double Score,
    string? Email,
    string? Phone,
    string? Domain,
    Guid? CompanyId);

public sealed record DuplicateCheckResponse(bool HasBlocking, IReadOnlyList<DuplicateMatchDto> Matches);

/// <summary>DUP-4: the caller picks the survivor and, field by field, which value to keep.</summary>
public sealed record MergeRequest
{
    public Guid SurvivorId { get; init; }
    public Guid LoserId { get; init; }

    /// <summary>Field name to "survivor" or "loser"; anything omitted keeps the survivor's value.</summary>
    public Dictionary<string, string>? FieldChoices { get; init; }
}

public sealed record MergeResultDto(
    Guid SurvivorId,
    Guid LoserId,
    IReadOnlyList<Guid> MovedContactIds,
    IReadOnlyList<string> Tags,
    int Version);

/// <summary>OWN-2: change the owner of up to <c>Customer:MaxBulkRecords</c> records.</summary>
public sealed record ReassignRequest
{
    /// <summary>contact | company.</summary>
    public string RecordType { get; init; } = string.Empty;
    public IReadOnlyList<Guid> RecordIds { get; init; } = [];
    public Guid? NewOwnerId { get; init; }
}

public sealed record BulkActionResultDto(int Succeeded, int Skipped, IReadOnlyList<BulkItemErrorDto> Errors);

public sealed record BulkItemErrorDto(Guid? Id, int? Index, string Message);

/// <summary>DEL-4.</summary>
public sealed record BulkDeleteRequest
{
    public string RecordType { get; init; } = string.Empty;
    public IReadOnlyList<Guid> RecordIds { get; init; } = [];
}

public sealed record RecycleBinItemDto(
    Guid Id,
    string RecordType,
    string Name,
    string? Email,
    string? Domain,
    Guid? OwnerId,
    string? OwnerName,
    DateTimeOffset DeletedAt,
    DateTimeOffset PurgeAfter,
    int Version);

public sealed record ReassignmentQueueItemDto(
    Guid Id,
    string RecordType,
    Guid RecordId,
    string Name,
    Guid PreviousOwnerId,
    string? PreviousOwnerName,
    DateTimeOffset QueuedAt);

/// <summary>§5 bulk upsert: one result per submitted row (AC-18).</summary>
public sealed record BulkUpsertRowResultDto(int Index, string Status, Guid? Id, string? Message);

public sealed record BulkUpsertResponse(
    int Created,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<BulkUpsertRowResultDto> Rows);

/// <summary>Custom field values as sent and returned: a flat JSON object keyed by field_key.</summary>
public sealed class CustomFieldValues : Dictionary<string, JsonElement>
{
    public CustomFieldValues() { }
    public CustomFieldValues(IDictionary<string, JsonElement> source) : base(source) { }
}
