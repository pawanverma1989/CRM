namespace LeadApi.Domain.Entities;

/// <summary>Admin-managed list of reasons for disqualifying a lead (STA-3).</summary>
public class DisqualifyReason
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
