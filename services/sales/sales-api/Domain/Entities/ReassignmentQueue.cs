namespace SalesApi.Domain.Entities;

/// <summary>
/// Holds open deals that belonged to a deactivated user, pending manual reassignment (OWN-2).
/// Added by the event processor on user.deactivated; removed when the deal is reassigned.
/// </summary>
public class ReassignmentQueue
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DealId { get; set; }
    public Guid DeactivatedUserId { get; set; }
    public DateTimeOffset QueuedAt { get; set; }

    public Deal? Deal { get; set; }
}
