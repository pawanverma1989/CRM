namespace LeadApi.Application.DTOs;

public sealed record CustomFieldDefinitionDto(
    Guid Id,
    string EntityType,
    string FieldKey,
    string Label,
    string FieldType,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    int SortOrder,
    bool IsActive);

public sealed record CreateCustomFieldRequest
{
    public string EntityType { get; init; } = "lead";
    public string FieldKey { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string FieldType { get; init; } = string.Empty;
    public IReadOnlyList<string>? Options { get; init; }
    public bool IsRequired { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>CF-6: only label, options, order and the active flag may change after creation.</summary>
public sealed record UpdateCustomFieldRequest
{
    public string? Label { get; init; }
    public IReadOnlyList<string>? Options { get; init; }
    public bool? IsRequired { get; init; }
    public int? SortOrder { get; init; }
    public bool? IsActive { get; init; }
}
