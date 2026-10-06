namespace SalesApi.Domain.Entities;

/// <summary>
/// Local read-only copy of company/contact display names from the Customer service.
/// Fed by company.* and contact.* events; an event is applied only when its version
/// beats <see cref="SourceVersion"/> (CLAUDE.md rule 4, NFR-4).
/// entity_type is 'company' or 'contact'.
/// </summary>
public class CustomerRef
{
    public string EntityType { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsDeleted { get; set; }
    public int SourceVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
