namespace SalesApi.Application.DTOs;

// ── Kanban Board DTOs (BRD-1..BRD-5) ──────────────────────────────────────────

public sealed record BoardDealDto(
    Guid Id,
    string Name,
    string? CompanyName,
    decimal Amount,
    string Currency,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? ExpectedCloseDate,
    int DaysInStage,
    bool IsStale,
    bool IsOverdue,
    short Probability,
    string[] Tags);

public sealed record BoardColumnDto(
    Guid StageId,
    string StageName,
    int SortOrder,
    int DealCount,
    decimal TotalAmount,
    decimal WeightedForecast,
    IReadOnlyList<BoardDealDto> Deals);

public sealed record BoardPipelineDto(
    Guid Id,
    string Name,
    IReadOnlyList<PipelineStageDto> Stages);

public sealed record BoardResponse(
    BoardPipelineDto Pipeline,
    IReadOnlyList<BoardColumnDto> Columns);
