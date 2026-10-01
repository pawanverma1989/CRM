namespace IdentityApi.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? TeamId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string Role { get; set; } = "sales_rep";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Team? Team { get; set; }
    public ICollection<Team> ManagedTeams { get; set; } = [];
    public ICollection<UserSession> Sessions { get; set; } = [];
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = [];
}
