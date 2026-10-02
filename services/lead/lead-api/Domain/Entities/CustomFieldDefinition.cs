namespace LeadApi.Domain.Entities;

/// <summary>Admin-defined custom field for leads (CF-1). <c>FieldKey</c> and <c>FieldType</c> are immutable (CF-6).</summary>
public class CustomFieldDefinition
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>Always 'lead' for this service.</summary>
    public string EntityType { get; set; } = "lead";

    /// <summary>Matches <c>^[a-z][a-z0-9_]*$</c>.</summary>
    public string FieldKey { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>text | textarea | number | currency | date | datetime | boolean | select | multiselect | email | phone | url | user.</summary>
    public string FieldType { get; set; } = string.Empty;

    /// <summary>JSONB array of allowed values for select / multiselect.</summary>
    public string? Options { get; set; }

    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Deactivating hides the field but keeps stored values (CF-5).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
