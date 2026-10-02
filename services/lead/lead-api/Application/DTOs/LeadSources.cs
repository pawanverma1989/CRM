namespace LeadApi.Application.DTOs;

public sealed record LeadSourceDto(Guid Id, string Name, bool IsActive);

public sealed record CreateLeadSourceRequest
{
    public string Name { get; init; } = string.Empty;
}

public sealed record UpdateLeadSourceRequest
{
    public string? Name { get; init; }
    public bool? IsActive { get; init; }
}
