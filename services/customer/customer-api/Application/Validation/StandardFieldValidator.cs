namespace CustomerApi.Application.Validation;
using System.Text.RegularExpressions;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Normalization;

/// <summary>
/// §4 standard-field rules, checked before the database CHECK constraints so the client gets a
/// 400 naming the field rather than a raw constraint violation. Every text field is trimmed and an
/// empty string is stored as no value.
/// </summary>
public static partial class StandardFieldValidator
{
    [GeneratedRegex(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$", RegexOptions.CultureInvariant)]
    private static partial Regex GstinPattern();

    [GeneratedRegex(@"^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();

    [GeneratedRegex(@"^[0-9]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex IndiaPostalPattern();

    /// <summary>Trims and turns a blank string into <c>null</c>.</summary>
    public static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public sealed class Collector
    {
        private readonly Dictionary<string, List<string>> _errors = [];

        public void Add(string field, string message)
        {
            if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
            list.Add(message);
        }

        public bool HasErrors => _errors.Count > 0;

        /// <summary>
        /// Folds in the errors from a helper that throws instead of collecting (tags, custom
        /// fields), so one save reports every bad field at once rather than the first.
        /// </summary>
        public void Merge(CustomerValidationException exception)
        {
            foreach (var (field, messages) in exception.Errors)
                foreach (var message in messages)
                    Add(field, message);
        }

        public void ThrowIfInvalid()
        {
            if (!HasErrors) return;
            throw new CustomerValidationException(
                _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }

        /// <summary>Collected errors, used by bulk import to report one message per row.</summary>
        public string Summary() =>
            string.Join("; ", _errors.SelectMany(e => e.Value.Select(m => $"{e.Key}: {m}")));
    }

    public static string? Text(Collector errors, string field, string? value, int maxLength, bool required = false)
    {
        var cleaned = Clean(value);

        if (required && cleaned is null)
        {
            errors.Add(field, "This field is required.");
            return null;
        }

        if (cleaned is not null && cleaned.Length > maxLength)
            errors.Add(field, $"Must be at most {maxLength} characters.");

        return cleaned;
    }

    /// <summary>COM-4: normalises and validates a company's web domain.</summary>
    public static string? Domain(Collector errors, string? value)
    {
        var normalized = DomainNormalizer.Normalize(value);
        if (normalized is null) return null;

        if (normalized.Length > 253)
            errors.Add("domain", "Must be at most 253 characters.");
        else if (!DomainNormalizer.IsValid(normalized))
            errors.Add("domain", $"'{normalized}' is not a valid web domain.");

        return normalized;
    }

    public static string? Email(Collector errors, string? value)
    {
        var cleaned = EmailValidator.Clean(value);
        if (cleaned is null) return null;

        if (cleaned.Length > EmailValidator.MaxLength)
            errors.Add("email", $"Must be at most {EmailValidator.MaxLength} characters.");
        else if (!EmailValidator.IsValid(cleaned))
            errors.Add("email", "Not a valid email address.");

        return cleaned;
    }

    /// <summary>A phone kept as entered; <see cref="PhoneNormalizer"/> supplies the E.164 copy (CON-4).</summary>
    public static string? Phone(Collector errors, string field, string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is null) return null;

        if (cleaned.Length > 30)
            errors.Add(field, "Must be at most 30 characters.");
        else if (!PhoneNormalizer.IsValid(cleaned))
            errors.Add(field, "Not a valid phone number.");

        return cleaned;
    }

    public static string? Website(Collector errors, string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is null) return null;

        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add("website", "Must start with http:// or https://.");

        return cleaned;
    }

    public static string? Country(Collector errors, string? value, string fallback)
    {
        var cleaned = Clean(value)?.ToUpperInvariant() ?? fallback;
        if (!CountryPattern().IsMatch(cleaned))
        {
            errors.Add("country", "Must be a two-letter ISO 3166 country code.");
            return null;
        }
        return cleaned;
    }

    /// <summary>§4.1: a postal code must be six digits when the country is India.</summary>
    public static string? PostalCode(Collector errors, string? value, string? country)
    {
        var cleaned = Text(errors, "postalCode", value, 20);
        if (cleaned is null) return null;

        if (country == "IN" && !IndiaPostalPattern().IsMatch(cleaned))
            errors.Add("postalCode", "An Indian postal code is six digits.");

        return cleaned;
    }

    public static string? Gstin(Collector errors, string? value)
    {
        var cleaned = Clean(value)?.ToUpperInvariant();
        if (cleaned is null) return null;

        if (cleaned.Length != 15 || !GstinPattern().IsMatch(cleaned))
            errors.Add("gstin", "Not a valid 15-character GSTIN.");

        return cleaned;
    }

    public static int? EmployeeCount(Collector errors, int? value)
    {
        if (value is < 0) errors.Add("employeeCount", "Must be 0 or more.");
        return value;
    }

    public static decimal? Money(Collector errors, string field, decimal? value)
    {
        if (value is null) return null;
        if (value < 0) errors.Add(field, "Must be 0 or more.");
        return Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);
    }
}
