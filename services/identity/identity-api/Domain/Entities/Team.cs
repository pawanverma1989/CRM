namespace IdentityApi.Domain.Entities;

public class Team
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ManagerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public User? Manager { get; set; }
    public ICollection<User> Members { get; set; } = [];
}
