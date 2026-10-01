namespace IdentityApi.Domain.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DefaultCurrency { get; set; } = "INR";
    public string Timezone { get; set; } = "Asia/Kolkata";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Team> Teams { get; set; } = [];
    public ICollection<User> Users { get; set; } = [];
}
