namespace LeadApi.Application.DTOs;

public sealed record DisqualifyReasonDto(Guid Id, string Name, bool IsActive);

public sealed record CreateDisqualifyReasonRequest
{
    public string Name { get; init; } = string.Empty;
}

public sealed record UpdateDisqualifyReasonRequest
{
    public string? Name { get; init; }
    public bool? IsActive { get; init; }
}
