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
    public string Status { get; set; } = "active";
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public string? PendingEmail { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    /// <summary>True for users created by an admin with an initial password; cleared on password change/reset.</summary>
    public bool MustChangePassword { get; set; }
    /// <summary>
    /// Aggregate version (V4). Store-generated: the BEFORE UPDATE trigger sets it to OLD.version + 1 on
    /// every update and EF reads the new value back. Carried in every user.* event payload.
    /// </summary>
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
    public Team? Team { get; set; }
    public ICollection<Team> ManagedTeams { get; set; } = [];
    public ICollection<UserSession> Sessions { get; set; } = [];
    public ICollection<UserToken> UserTokens { get; set; } = [];
}
