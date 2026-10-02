namespace CustomerApi.Application.Validation;
using System.Text.RegularExpressions;

/// <summary>
/// Email syntax check for the standard <c>contacts.email</c> column (§4.2) and for custom fields
/// of type <c>email</c> (CF-2). Emails are stored as entered and compared case-insensitively
/// (<c>citext</c>, CON-3), so nothing here lower-cases the value — it only decides whether the
/// address is well formed and within the 254-character column limit.
/// </summary>
public static partial class EmailValidator
{
    /// <summary>§4.2: Email, max 254.</summary>
    public const int MaxLength = 254;

    // Deliberately conservative: one @, a dot-separated local part with no leading, trailing or
    // doubled dots, and a domain label chain ending in a 2+ character TLD.
    [GeneratedRegex(
        @"^[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+(\.[a-zA-Z0-9!#$%&'*+/=?^_`{|}~-]+)*@((?!-)[a-zA-Z0-9-]{1,63}(?<!-)\.)+[a-zA-Z]{2,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    /// <summary>True when <paramref name="raw"/> is a usable address. Null, blank and over-long values are not.</summary>
    public static bool IsValid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim();
        return value.Length <= MaxLength && EmailPattern().IsMatch(value);
    }

    /// <summary>Trims and collapses an empty string to <c>null</c> (§4: "empty strings are stored as no value").</summary>
    public static string? Clean(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
}
