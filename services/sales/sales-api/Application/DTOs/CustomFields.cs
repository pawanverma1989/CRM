namespace SalesApi.Application.DTOs;

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

public sealed record CreateCustomFieldRequest(
    string EntityType,
    string FieldKey,
    string Label,
    string FieldType,
    IReadOnlyList<string>? Options = null,
    bool IsRequired = false,
    int SortOrder = 0);

public sealed record UpdateCustomFieldRequest(
    string? Label = null,
    IReadOnlyList<string>? Options = null,
    bool? IsRequired = null,
    int? SortOrder = null,
    bool? IsActive = null);

/// <summary>Loss reason DTOs.</summary>
public sealed record LossReasonDto(Guid Id, Guid OrganizationId, string Name, bool IsActive);

public sealed record CreateLossReasonRequest(string Name);

public sealed record UpdateLossReasonRequest(string? Name = null, bool? IsActive = null);
