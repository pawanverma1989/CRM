namespace SalesApi.Domain.Entities;

/// <summary>
/// A stage within a pipeline. stage_type is open|won|lost; exactly one won and one lost
/// per pipeline (enforced by V2 partial unique indexes PIP-3).
/// </summary>
public class PipelineStage
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    /// <summary>0–100 default probability for deals in this stage.</summary>
    public short Probability { get; set; }

    /// <summary>open | won | lost (CHECK constraint in DB).</summary>
    public string StageType { get; set; } = "open";

    public bool IsActive { get; set; } = true;

    public Pipeline? Pipeline { get; set; }
}
