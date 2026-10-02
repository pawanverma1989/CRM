namespace IdentityApi.Domain.Entities;

public class ServiceClient
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Organization Organization { get; set; } = null!;
}
