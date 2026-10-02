namespace LeadApi.Domain.Entities;

/// <summary>Admin-managed list of lead sources (e.g. website, referral, cold outreach).</summary>
public class LeadSource
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
