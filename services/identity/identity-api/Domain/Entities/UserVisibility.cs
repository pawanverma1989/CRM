namespace IdentityApi.Domain.Entities;

public class UserVisibility
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Role { get; set; } = string.Empty;
    public Guid[]? VisibleOwnerIds { get; set; }
}
