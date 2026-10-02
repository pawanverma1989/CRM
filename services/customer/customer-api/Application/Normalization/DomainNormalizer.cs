namespace CustomerApi.Application.Normalization;
using System.Text.RegularExpressions;

/// <summary>
/// COM-4: a company's web domain is stored lower-case with no scheme, no <c>www.</c>, no port,
/// no path and no trailing dot. "https://www.Acme.com/" becomes "acme.com".
/// </summary>
public static partial class DomainNormalizer
{
    [GeneratedRegex(@"^(?=.{1,253}$)(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))*\.[a-z]{2,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim().ToLowerInvariant();

        // scheme
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0) value = value[(schemeEnd + 3)..];

        // credentials
        var at = value.IndexOf('@');
        if (at >= 0) value = value[(at + 1)..];

        // path / query / fragment
        var cut = value.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) value = value[..cut];

        // port
        var colon = value.IndexOf(':');
        if (colon >= 0) value = value[..colon];

        if (value.StartsWith("www.", StringComparison.Ordinal)) value = value[4..];

        value = value.Trim().TrimEnd('.');

        return value.Length == 0 ? null : value;
    }

    /// <summary>True when the already-normalised value looks like a registrable domain name.</summary>
    public static bool IsValid(string? normalized)
        => !string.IsNullOrEmpty(normalized) && DomainPattern().IsMatch(normalized);
}
