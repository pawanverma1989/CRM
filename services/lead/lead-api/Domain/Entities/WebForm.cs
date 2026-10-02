namespace LeadApi.Domain.Entities;

/// <summary>
/// A public web form that creates or updates leads on submission (WEB-1).
/// <see cref="PublicKey"/> is the URL-safe identifier exposed to the embedding snippet.
/// </summary>
public class WebForm
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Opaque, URL-safe key used in the public submission URL.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>JSONB array of field descriptors shown on the form.</summary>
    public string Fields { get; set; } = "[]";

    /// <summary>JSONB array of required field keys for this form.</summary>
    public string RequiredFields { get; set; } = "[]";

    public Guid? LeadSourceId { get; set; }

    /// <summary>Default owner assigned to leads created from this form.</summary>
    public Guid? DefaultOwnerId { get; set; }

    public string? ConsentText { get; set; }
    public string? SuccessMessage { get; set; }
    public string? RedirectUrl { get; set; }

    /// <summary>WEB-4: whether CAPTCHA is required for submission.</summary>
    public bool CaptchaEnabled { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
