namespace LeadApi.Domain.Entities;

/// <summary>Records each public web form submission for rate limiting and audit (WEB-4).</summary>
public class WebFormSubmission
{
    public Guid Id { get; set; }

    /// <summary>FK to web_forms within this database.</summary>
    public Guid WebFormId { get; set; }
    public WebForm? WebForm { get; set; }

    /// <summary>FK to leads within this database.</summary>
    public Guid LeadId { get; set; }
    public Lead? Lead { get; set; }

    /// <summary>Stored as INET for PostgreSQL IP address type; represented as string in .NET.</summary>
    public string? IpAddress { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }
}
