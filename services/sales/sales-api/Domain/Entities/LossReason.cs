namespace SalesApi.Domain.Entities;

/// <summary>A reason for losing a deal. Required when marking a deal lost (WL-2).</summary>
public class LossReason
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
