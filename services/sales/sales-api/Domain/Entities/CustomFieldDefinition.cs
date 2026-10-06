namespace SalesApi.Domain.Entities;

/// <summary>
/// Definition of a custom field for the deal entity.
/// field_key must match ^[a-z][a-z0-9_]*$ (enforced by CHECK constraint).
/// </summary>
public class CustomFieldDefinition
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string EntityType { get; set; } = "deal";
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public string? Options { get; set; }
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
