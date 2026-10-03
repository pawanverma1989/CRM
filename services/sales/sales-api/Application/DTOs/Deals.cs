namespace SalesApi.Application.DTOs;

// ── Deal DTOs ──────────────────────────────────────────────────────────────────

public sealed record DealContactDto(
    Guid ContactId,
    string? Role,
    bool IsPrimary,
    string? ContactName);

public sealed record StageHistoryDto(
    Guid Id,
    Guid? FromStageId,
    string? FromStageName,
    Guid ToStageId,
    string ToStageName,
    decimal? AmountAtChange,
    Guid? ChangedBy,
    DateTimeOffset ChangedAt);

public sealed record DealDto(
    Guid Id,
    Guid OrganizationId,
    Guid PipelineId,
    string? PipelineName,
    Guid StageId,
    string? StageName,
    Guid? OwnerId,
    string? OwnerName,
    Guid? CompanyId,
    string? CompanyName,
    Guid? PrimaryContactId,
    string? PrimaryContactName,
    string Name,
    decimal Amount,
    string Currency,
    short? Probability,
    DateOnly? ExpectedCloseDate,
    string Status,
    DateTimeOffset? ClosedAt,
    Guid? LossReasonId,
    string? LossReasonName,
    string? LossNotes,
    Guid? SourceLeadId,
    DateTimeOffset StageEnteredAt,
    DateTimeOffset? LastActivityAt,
    string[] Tags,
    CustomFieldValues? CustomFields,
    int Version,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<DealContactDto>? Contacts,
    IReadOnlyList<StageHistoryDto>? History,
    bool IsStale,
    bool IsOverdue);

public sealed record RecycleBinDealDto(
    Guid Id,
    string Name,
    Guid? OwnerId,
    string Status,
    DateTimeOffset DeletedAt,
    DateTimeOffset PurgeAfter,
    int Version);

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record CreateDealRequest(
    Guid PipelineId,
    Guid StageId,
    string Name,
    decimal Amount = 0,
    string Currency = "INR",
    Guid? OwnerId = null,
    Guid? CompanyId = null,
    Guid? PrimaryContactId = null,
    short? Probability = null,
    DateOnly? ExpectedCloseDate = null,
    string[]? Tags = null,
    CustomFieldValues? CustomFields = null,
    Guid? SourceLeadId = null);

public sealed record UpdateDealRequest(
    int Version,
    string? Name = null,
    decimal? Amount = null,
    string? Currency = null,
    Guid? OwnerId = null,
    Guid? CompanyId = null,
    Guid? PrimaryContactId = null,
    short? Probability = null,
    DateOnly? ExpectedCloseDate = null,
    string[]? Tags = null,
    CustomFieldValues? CustomFields = null,
    string? LossNotes = null);

public sealed record MoveDealRequest(Guid StageId);

public sealed record MarkWonRequest(DateOnly? CloseDate = null);

public sealed record MarkLostRequest(
    Guid LossReasonId,
    string? LossNotes = null,
    DateOnly? CloseDate = null);

public sealed record ReopenDealRequest(Guid StageId);

public sealed record DealContactEntry(Guid ContactId, string? Role, bool IsPrimary = false);

public sealed record ReplaceDealContactsRequest(IReadOnlyList<DealContactEntry> Contacts);

public sealed record BulkReassignRequest(
    IReadOnlyList<Guid> DealIds,
    Guid? NewOwnerId);

public sealed record BulkReassignResult(int Reassigned, int Skipped);

// ── List query ─────────────────────────────────────────────────────────────────

public sealed record DealListQuery(
    int? Page = null,
    int? PageSize = null,
    Guid? PipelineId = null,
    Guid? StageId = null,
    Guid? OwnerId = null,
    string? Status = null,
    bool? Unassigned = null,
    DateOnly? CloseDateFrom = null,
    DateOnly? CloseDateTo = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string[]? Tags = null,
    string? Q = null,
    string? SortBy = null,
    string? SortDir = null,
    bool? IsStale = null);
