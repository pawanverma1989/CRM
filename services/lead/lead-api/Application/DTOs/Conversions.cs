namespace LeadApi.Application.DTOs;

/// <summary>Request to start a lead conversion saga.</summary>
public sealed record ConversionRequest
{
    /// <summary>Whether to create a company in the Customer service.</summary>
    public bool CreateCompany { get; init; } = true;

    /// <summary>Whether to create a deal in the Sales service.</summary>
    public bool CreateDeal { get; init; } = true;

    /// <summary>Optional company name override (defaults to lead's company_name).</summary>
    public string? CompanyName { get; init; }

    /// <summary>Optional deal name.</summary>
    public string? DealName { get; init; }

    public string? Currency { get; init; }
}

/// <summary>Current state of a lead conversion saga.</summary>
public sealed record ConversionDto(
    Guid Id,
    Guid LeadId,
    string Status,
    Guid? CompanyId,
    Guid? ContactId,
    Guid? DealId,
    string? LastError,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
