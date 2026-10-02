namespace LeadApi.Application.Normalization;
using System.Text;

/// <summary>
/// Phone numbers are kept as entered and also stored in E.164 form in
/// <c>phone_normalized</c>. A number with no country code is assumed Indian (+91).
/// </summary>
public static class PhoneNormalizer
{
    public const string DefaultCountryCode = "91";

    private const int MinE164Digits = 8;
    private const int MaxE164Digits = 15;

    public static string? Normalize(string? raw, string defaultCountryCode = DefaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var trimmed = raw.Trim();
        var explicitInternational = trimmed.StartsWith('+') || trimmed.StartsWith("(+", StringComparison.Ordinal);

        var digits = Digits(trimmed);
        if (digits.Length == 0) return null;

        if (!explicitInternational)
        {
            if (digits.StartsWith("00", StringComparison.Ordinal))
            {
                digits = digits[2..];
                explicitInternational = true;
            }
            else
            {
                digits = digits.TrimStart('0');
                if (digits.Length == 0) return null;

                if (digits.Length == 10)
                    digits = defaultCountryCode + digits;
                else if (!digits.StartsWith(defaultCountryCode, StringComparison.Ordinal))
                    return null;
            }
        }

        return digits.Length is >= MinE164Digits and <= MaxE164Digits ? "+" + digits : null;
    }

    public static bool IsValid(string? raw) => Normalize(raw) is not null;

    private static string Digits(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            if (char.IsAsciiDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}
