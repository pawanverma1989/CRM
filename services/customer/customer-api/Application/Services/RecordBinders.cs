namespace CustomerApi.Application.Services;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Normalization;
using CustomerApi.Application.Validation;
using CustomerApi.Domain.Entities;
using CustomerApi.Settings;
using Collector = CustomerApi.Application.Validation.StandardFieldValidator.Collector;

/// <summary>
/// Reference data a write needs, loaded once per request (or once per import batch) so validating
/// 1,000 rows does not mean 1,000 lookups (NFR-3).
/// </summary>
public sealed record WriteReference(
    Guid OrganizationId,
    IReadOnlyCollection<CustomFieldDefinition> ActiveCustomFields,
    IReadOnlySet<Guid> IndustryIds,
    IReadOnlySet<Guid> SourceIds,
    CustomerSettings Settings);

/// <summary>
/// Maps requests onto entities, applying the §4 field rules. Shared by the CRUD services and the
/// bulk importer so an imported row is validated exactly like a hand-typed one (CF-3).
/// </summary>
public static class RecordBinders
{
    public static void ApplyCreate(Company e, CreateCompanyRequest r, WriteReference reference, Collector errors)
    {
        e.Name = StandardFieldValidator.Text(errors, "name", r.Name, 200, required: true) ?? string.Empty;
        e.Domain = StandardFieldValidator.Domain(errors, r.Domain);
        e.IndustryId = Picklist(errors, "industryId", r.IndustryId, reference.IndustryIds, "industry");
        e.EmployeeCount = StandardFieldValidator.EmployeeCount(errors, r.EmployeeCount);
        e.AnnualRevenue = StandardFieldValidator.Money(errors, "annualRevenue", r.AnnualRevenue);
        e.Phone = StandardFieldValidator.Phone(errors, "phone", r.Phone);
        e.Website = StandardFieldValidator.Website(errors, r.Website);
        e.AddressLine1 = StandardFieldValidator.Text(errors, "addressLine1", r.AddressLine1, 200);
        e.AddressLine2 = StandardFieldValidator.Text(errors, "addressLine2", r.AddressLine2, 200);
        e.City = StandardFieldValidator.Text(errors, "city", r.City, 100);
        e.State = StandardFieldValidator.Text(errors, "state", r.State, 100);
        e.Country = StandardFieldValidator.Country(errors, r.Country, reference.Settings.DefaultCountry);
        e.PostalCode = StandardFieldValidator.PostalCode(errors, r.PostalCode, e.Country);
        e.Gstin = StandardFieldValidator.Gstin(errors, r.Gstin);
        e.Tags = Tags(errors, r.Tags, reference.Settings);
        e.CustomFields = CustomFields(errors, reference, r.CustomFields, e.CustomFields, isNewRecord: true);
    }

    public static void ApplyPatch(Company e, UpdateCompanyRequest r, WriteReference reference, Collector errors)
    {
        if (r.Name.HasValue)
            e.Name = StandardFieldValidator.Text(errors, "name", r.Name.Value, 200, required: true) ?? e.Name;

        if (r.Domain.HasValue) e.Domain = StandardFieldValidator.Domain(errors, r.Domain.Value);

        if (r.IndustryId.HasValue)
            e.IndustryId = Picklist(errors, "industryId", r.IndustryId.Value, reference.IndustryIds, "industry");

        if (r.EmployeeCount.HasValue) e.EmployeeCount = StandardFieldValidator.EmployeeCount(errors, r.EmployeeCount.Value);
        if (r.AnnualRevenue.HasValue) e.AnnualRevenue = StandardFieldValidator.Money(errors, "annualRevenue", r.AnnualRevenue.Value);
        if (r.Phone.HasValue) e.Phone = StandardFieldValidator.Phone(errors, "phone", r.Phone.Value);
        if (r.Website.HasValue) e.Website = StandardFieldValidator.Website(errors, r.Website.Value);
        if (r.AddressLine1.HasValue) e.AddressLine1 = StandardFieldValidator.Text(errors, "addressLine1", r.AddressLine1.Value, 200);
        if (r.AddressLine2.HasValue) e.AddressLine2 = StandardFieldValidator.Text(errors, "addressLine2", r.AddressLine2.Value, 200);
        if (r.City.HasValue) e.City = StandardFieldValidator.Text(errors, "city", r.City.Value, 100);
        if (r.State.HasValue) e.State = StandardFieldValidator.Text(errors, "state", r.State.Value, 100);
        if (r.Country.HasValue) e.Country = StandardFieldValidator.Country(errors, r.Country.Value, reference.Settings.DefaultCountry);
        if (r.PostalCode.HasValue) e.PostalCode = StandardFieldValidator.PostalCode(errors, r.PostalCode.Value, e.Country);
        if (r.Gstin.HasValue) e.Gstin = StandardFieldValidator.Gstin(errors, r.Gstin.Value);
        if (r.Tags.HasValue) e.Tags = Tags(errors, r.Tags.Value, reference.Settings);

        // CF-4: a field made required later must not block other edits to an older record.
        if (r.CustomFields.HasValue)
            e.CustomFields = CustomFields(errors, reference, r.CustomFields.Value, e.CustomFields, isNewRecord: false);
    }

    public static void ApplyCreate(Contact e, CreateContactRequest r, WriteReference reference, Collector errors)
    {
        e.FirstName = StandardFieldValidator.Text(errors, "firstName", r.FirstName, 100, required: true) ?? string.Empty;
        e.LastName = StandardFieldValidator.Text(errors, "lastName", r.LastName, 100);
        e.Email = StandardFieldValidator.Email(errors, r.Email);
        e.Phone = StandardFieldValidator.Phone(errors, "phone", r.Phone);
        e.PhoneNormalized = PhoneNormalizer.Normalize(e.Phone, reference.Settings.DefaultPhoneCountryCode);
        e.Mobile = StandardFieldValidator.Phone(errors, "mobile", r.Mobile);
        e.JobTitle = StandardFieldValidator.Text(errors, "jobTitle", r.JobTitle, 150);
        e.SourceId = Picklist(errors, "sourceId", r.SourceId, reference.SourceIds, "source");
        e.AddressLine1 = StandardFieldValidator.Text(errors, "addressLine1", r.AddressLine1, 200);
        e.AddressLine2 = StandardFieldValidator.Text(errors, "addressLine2", r.AddressLine2, 200);
        e.City = StandardFieldValidator.Text(errors, "city", r.City, 100);
        e.State = StandardFieldValidator.Text(errors, "state", r.State, 100);
        e.Country = StandardFieldValidator.Country(errors, r.Country, reference.Settings.DefaultCountry);
        e.PostalCode = StandardFieldValidator.PostalCode(errors, r.PostalCode, e.Country);
        e.Tags = Tags(errors, r.Tags, reference.Settings);
        e.CustomFields = CustomFields(errors, reference, r.CustomFields, e.CustomFields, isNewRecord: true);

        RequireEmailOrPhone(e, errors);
    }

    public static void ApplyPatch(Contact e, UpdateContactRequest r, WriteReference reference, Collector errors)
    {
        if (r.FirstName.HasValue)
            e.FirstName = StandardFieldValidator.Text(errors, "firstName", r.FirstName.Value, 100, required: true) ?? e.FirstName;

        if (r.LastName.HasValue) e.LastName = StandardFieldValidator.Text(errors, "lastName", r.LastName.Value, 100);
        if (r.Email.HasValue) e.Email = StandardFieldValidator.Email(errors, r.Email.Value);

        if (r.Phone.HasValue)
        {
            e.Phone = StandardFieldValidator.Phone(errors, "phone", r.Phone.Value);
            e.PhoneNormalized = PhoneNormalizer.Normalize(e.Phone, reference.Settings.DefaultPhoneCountryCode);
        }

        if (r.Mobile.HasValue) e.Mobile = StandardFieldValidator.Phone(errors, "mobile", r.Mobile.Value);
        if (r.JobTitle.HasValue) e.JobTitle = StandardFieldValidator.Text(errors, "jobTitle", r.JobTitle.Value, 150);
        if (r.SourceId.HasValue) e.SourceId = Picklist(errors, "sourceId", r.SourceId.Value, reference.SourceIds, "source");
        if (r.AddressLine1.HasValue) e.AddressLine1 = StandardFieldValidator.Text(errors, "addressLine1", r.AddressLine1.Value, 200);
        if (r.AddressLine2.HasValue) e.AddressLine2 = StandardFieldValidator.Text(errors, "addressLine2", r.AddressLine2.Value, 200);
        if (r.City.HasValue) e.City = StandardFieldValidator.Text(errors, "city", r.City.Value, 100);
        if (r.State.HasValue) e.State = StandardFieldValidator.Text(errors, "state", r.State.Value, 100);
        if (r.Country.HasValue) e.Country = StandardFieldValidator.Country(errors, r.Country.Value, reference.Settings.DefaultCountry);
        if (r.PostalCode.HasValue) e.PostalCode = StandardFieldValidator.PostalCode(errors, r.PostalCode.Value, e.Country);
        if (r.Tags.HasValue) e.Tags = Tags(errors, r.Tags.Value, reference.Settings);

        if (r.CustomFields.HasValue)
            e.CustomFields = CustomFields(errors, reference, r.CustomFields.Value, e.CustomFields, isNewRecord: false);

        RequireEmailOrPhone(e, errors);
    }

    /// <summary>CON-1 / AC-2: a contact needs an email or a phone, and the error names both.</summary>
    public static void RequireEmailOrPhone(Contact e, Collector errors)
    {
        if (e.Email is null && e.Phone is null)
        {
            errors.Add("email", "A contact needs an email or a phone number.");
            errors.Add("phone", "A contact needs an email or a phone number.");
        }
    }

    private static Guid? Picklist(
        Collector errors, string field, Guid? value, IReadOnlySet<Guid> allowed, string listLabel)
    {
        if (value is null) return null;

        if (!allowed.Contains(value.Value))
        {
            errors.Add(field, $"Not an active {listLabel} value for this organization.");
            return null;
        }
        return value;
    }

    private static string[] Tags(Collector errors, IReadOnlyList<string>? tags, CustomerSettings settings)
    {
        try { return TagNormalizer.Normalize(tags, settings.MaxTagsPerRecord); }
        catch (CustomerValidationException ex) { errors.Merge(ex); return []; }
    }

    private static string CustomFields(
        Collector errors,
        WriteReference reference,
        CustomFieldValues? incoming,
        string existing,
        bool isNewRecord)
    {
        try
        {
            return CustomFieldValidator.Validate(reference.ActiveCustomFields, incoming, existing, isNewRecord);
        }
        catch (CustomerValidationException ex)
        {
            errors.Merge(ex);
            return existing;
        }
    }
}
