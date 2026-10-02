namespace CustomerApi.Application.Queries;
using CustomerApi.Application.DTOs;
using CustomerApi.Settings;

/// <summary>LST-1 / LST-3: page size clamping and the sort whitelist, shared by both list endpoints.</summary>
public static class ListPaging
{
    public static readonly string[] SortFields = ["name", "created", "updated", "owner"];

    /// <summary>
    /// LST-1: 50 per page by default, never more than 200. <paramref name="maxPageSizeOverride"/>
    /// raises that ceiling for <c>GET /export</c>, which streams a whole import-sized batch.
    /// </summary>
    public static (int Page, int PageSize) Resolve(
        ListQueryBase query, CustomerSettings settings, int? maxPageSizeOverride = null)
    {
        var max = maxPageSizeOverride is > 0 ? maxPageSizeOverride.Value : settings.MaxPageSize;
        var page = query.Page < 1 ? 1 : query.Page;
        var size = query.PageSize is null or < 1 ? settings.DefaultPageSize : query.PageSize.Value;
        if (size > max) size = max;
        return (page, size);
    }

    /// <summary>Unknown sort keys fall back to <c>name</c> rather than being rejected (LST-3).</summary>
    public static (string Field, bool Descending) ResolveSort(ListQueryBase query)
    {
        var field = (query.Sort ?? "name").Trim().ToLowerInvariant();
        if (!SortFields.Contains(field)) field = "name";
        var descending = string.Equals(query.Direction, "desc", StringComparison.OrdinalIgnoreCase);
        return (field, descending);
    }

    /// <summary>Escapes a quick-search term for use inside a <c>%…%</c> ILIKE pattern (LST-4).</summary>
    public static string LikePattern(string term)
        => "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>
    /// The two JSONB fragments a <c>?customField=key:value</c> filter is matched against with
    /// <c>@&gt;</c> (LST-2): the scalar form for single-value fields and the array form for
    /// multi-selects, where jsonb containment already means "holds this element". Both use the
    /// GIN index on <c>custom_fields</c>. A value that parses as a number or a boolean is written
    /// as one, so <c>?customField=seats:5</c> matches the stored number 5.
    /// </summary>
    public static (string Scalar, string Array) CustomFieldJson(string key, string value)
    {
        var encodedKey = System.Text.Json.JsonEncodedText.Encode(key).ToString();
        var token = value switch
        {
            "true" or "false" or "null" => value,
            _ when decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                       System.Globalization.CultureInfo.InvariantCulture, out _) => value,
            _ => "\"" + System.Text.Json.JsonEncodedText.Encode(value) + "\""
        };

        return ($"{{\"{encodedKey}\":{token}}}", $"{{\"{encodedKey}\":[{token}]}}");
    }

    /// <summary>
    /// Parses the repeatable <c>?customField=key:value</c> filter (LST-2). MVP semantics are
    /// scalar equality against the stored JSON value, compared as text.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value)> CustomFieldFilters(string[]? raw)
    {
        if (raw is null || raw.Length == 0) return [];

        var result = new List<(string, string)>();
        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var split = entry.IndexOf(':');
            if (split <= 0 || split == entry.Length - 1) continue;
            result.Add((entry[..split].Trim(), entry[(split + 1)..].Trim()));
        }
        return result;
    }
}
