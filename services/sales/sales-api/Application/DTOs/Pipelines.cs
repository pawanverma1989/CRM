namespace SalesApi.Application.DTOs;

// ── Pipeline DTOs ──────────────────────────────────────────────────────────────

public sealed record PipelineStageDto(
    Guid Id,
    Guid PipelineId,
    string Name,
    int SortOrder,
    short Probability,
    string StageType,
    bool IsActive);

public sealed record PipelineDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PipelineStageDto> Stages);

// ── Requests ───────────────────────────────────────────────────────────────────

public sealed record CreatePipelineRequest(
    string Name,
    bool IsDefault = false);

public sealed record UpdatePipelineRequest(
    string? Name,
    bool? IsDefault);

public sealed record CreateStageRequest(
    string Name,
    short Probability,
    string StageType = "open");

public sealed record UpdateStageRequest(
    string? Name,
    int? SortOrder,
    short? Probability,
    bool? IsActive);

// StageIds: list of stage IDs in the desired order (first = sort_order 1).
public sealed record ReorderStagesRequest(IReadOnlyList<Guid> StageIds);
