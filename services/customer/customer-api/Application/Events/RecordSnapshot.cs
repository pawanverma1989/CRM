namespace CustomerApi.Application.Events;
using CustomerApi.Domain.Entities;

/// <summary>
/// Field snapshots used to build the <c>changes</c> list carried by <c>company.updated</c> and
/// <c>contact.updated</c> (§6.1). The compliance service writes those old/new pairs into
/// <c>audit_log.changes</c>, so the keys are the snake_case event field names.
/// </summary>
public static class RecordSnapshot
{
    public static Dictionary<string, object?> Of(Company c) => new()
    {
        ["owner_id"] = c.OwnerId,
        ["name"] = c.Name,
        ["domain"] = c.Domain,
        ["industry_id"] = c.IndustryId,
        ["employee_count"] = c.EmployeeCount,
        ["annual_revenue"] = c.AnnualRevenue,
        ["phone"] = c.Phone,
        ["website"] = c.Website,
        ["address_line1"] = c.AddressLine1,
        ["address_line2"] = c.AddressLine2,
        ["city"] = c.City,
        ["state"] = c.State,
        ["postal_code"] = c.PostalCode,
        ["country"] = c.Country,
        ["gstin"] = c.Gstin,
        ["tags"] = string.Join(',', c.Tags),
        ["custom_fields"] = c.CustomFields
    };

    public static Dictionary<string, object?> Of(Contact c) => new()
    {
        ["owner_id"] = c.OwnerId,
        ["company_id"] = c.CompanyId,
        ["first_name"] = c.FirstName,
        ["last_name"] = c.LastName,
        ["email"] = c.Email,
        ["phone"] = c.Phone,
        ["phone_normalized"] = c.PhoneNormalized,
        ["mobile"] = c.Mobile,
        ["job_title"] = c.JobTitle,
        ["address_line1"] = c.AddressLine1,
        ["address_line2"] = c.AddressLine2,
        ["city"] = c.City,
        ["state"] = c.State,
        ["postal_code"] = c.PostalCode,
        ["country"] = c.Country,
        ["source_id"] = c.SourceId,
        ["tags"] = string.Join(',', c.Tags),
        ["custom_fields"] = c.CustomFields
    };

    /// <summary>Fields whose value differs, with the old and the new value (§6.1).</summary>
    public static List<FieldChange> Diff(
        IReadOnlyDictionary<string, object?> before,
        IReadOnlyDictionary<string, object?> after)
    {
        var changes = new List<FieldChange>();
        foreach (var (field, newValue) in after)
        {
            before.TryGetValue(field, out var oldValue);
            if (!Equals(oldValue, newValue))
                changes.Add(new FieldChange(field, oldValue, newValue));
        }
        return changes;
    }
}

/// <summary>
/// The <c>*.updated</c> payload: the full record plus the changed fields (§6.1), so a consumer can
/// refresh its local copy and the compliance service can write a field-level audit entry.
/// </summary>
public sealed record UpdatedEventPayload<T>(T Record, IReadOnlyList<FieldChange> Changes);

/// <summary>The <c>*.deleted</c> payload (§6.1): the id and version only, never personal data.</summary>
public sealed record DeletedEventPayload(Guid Id, int Version);

/// <summary>The <c>*.merged</c> payload (§6.1, DUP-5).</summary>
public sealed record MergedEventPayload(
    Guid SurvivorId,
    Guid LoserId,
    IReadOnlyList<Guid> MovedContactIds,
    IReadOnlyList<string> Tags);

/// <summary>The <c>*.reassigned</c> payload (§6.1, OWN-2); the notification service tells the new owner.</summary>
public sealed record ReassignedEventPayload(Guid Id, Guid? FromOwnerId, Guid? ToOwnerId);

/// <summary>The <c>*.purged</c> payload (§6.1, DEL-3): the id of the permanently removed record.</summary>
public sealed record PurgedEventPayload(Guid Id, string Reason);

/// <summary>DSR-1 reply (§6.1).</summary>
public sealed record DsrErasureCompletedPayload(Guid DsrId, string Service, int RecordsAffected);

/// <summary>DSR-2 reply (§6.1).</summary>
public sealed record DsrAccessCompletedPayload(Guid DsrId, string Service, int RecordsAffected, string FileLocation);
