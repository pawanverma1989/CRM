namespace SalesApi.Domain.Entities;

/// <summary>
/// A sales pipeline (PIP-1..PIP-4). Each organization can have multiple pipelines.
/// There is at most one default pipeline per organization (enforced by partial unique index).
/// </summary>
public class Pipeline
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<PipelineStage> Stages { get; set; } = [];
}
