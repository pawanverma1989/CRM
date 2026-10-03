namespace SalesApi.Application.DTOs;
using System.Text.Json;

/// <summary>List response shape used by every list endpoint (LST-1).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Data, int Page, int PageSize, int Total);

/// <summary>Owner name for list display, served from the local <c>user_refs</c> copy.</summary>
public sealed record OwnerDto(Guid Id, string DisplayName, bool IsActive);

/// <summary>§5 bulk upsert: one result per submitted row.</summary>
public sealed record BulkUpsertRowResultDto(int Index, string Status, Guid? Id, string? Message);

public sealed record BulkUpsertResponse(
    int Created,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<BulkUpsertRowResultDto> Rows);

/// <summary>Custom field values as sent and returned: a flat JSON object keyed by field_key.</summary>
public sealed class CustomFieldValues : Dictionary<string, JsonElement>
{
    public CustomFieldValues() { }
    public CustomFieldValues(IDictionary<string, JsonElement> source) : base(source) { }
}
