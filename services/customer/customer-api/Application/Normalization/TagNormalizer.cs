namespace CustomerApi.Application.Normalization;
using CustomerApi.Application.Exceptions;

/// <summary>
/// TAG-2: tags are stored trimmed and lower-case, at most 40 characters each and at most
/// <c>maxPerRecord</c> per record. The <c>tags_are_valid()</c> CHECK in the database is the backstop.
/// </summary>
public static class TagNormalizer
{
    public const int MaxTagLength = 40;

    public static string[] Normalize(IEnumerable<string>? tags, int maxPerRecord)
    {
        if (tags is null) return [];

        var result = new List<string>();
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tag = raw.Trim().ToLowerInvariant();

            if (tag.Length > MaxTagLength)
                throw new CustomerValidationException("tags", $"Tag '{tag[..Math.Min(20, tag.Length)]}…' is longer than {MaxTagLength} characters.");

            if (!result.Contains(tag)) result.Add(tag);
        }

        if (result.Count > maxPerRecord)
            throw new CustomerValidationException("tags", $"A record may carry at most {maxPerRecord} tags.");

        return [.. result];
    }
}
