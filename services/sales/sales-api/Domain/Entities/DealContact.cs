namespace SalesApi.Domain.Entities;

/// <summary>
/// Associates a contact (from Customer service) with a deal, with an optional role.
/// At most one primary contact per deal (enforced by V2 partial unique index DL-2).
/// contact_id is a plain UUID, no FK across services (CLAUDE.md rule 2).
/// </summary>
public class DealContact
{
    public Guid DealId { get; set; }

    /// <summary>customer.contacts.id — plain UUID, no FK across services.</summary>
    public Guid ContactId { get; set; }

    public string? Role { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Deal? Deal { get; set; }
}
