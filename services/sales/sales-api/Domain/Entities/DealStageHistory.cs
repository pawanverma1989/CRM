namespace SalesApi.Domain.Entities;

/// <summary>
/// Immutable record of every stage transition for a deal.
/// Written by the DB trigger (deals_log_stage) on every INSERT or stage_id change.
/// </summary>
public class DealStageHistory
{
    public Guid Id { get; set; }
    public Guid DealId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public decimal? AmountAtChange { get; set; }
    public Guid? ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }

    public Deal? Deal { get; set; }
}
