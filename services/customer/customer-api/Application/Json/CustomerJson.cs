namespace CustomerApi.Application.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Event payloads use snake_case, matching the envelope in docs/architecture.md §4.3.
/// HTTP bodies keep ASP.NET Core's camelCase default.
/// </summary>
public static class CustomerJson
{
    public static readonly JsonSerializerOptions Events = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static readonly JsonSerializerOptions Api = new(JsonSerializerDefaults.Web);

    public static string SerializeEvent<T>(T value) => JsonSerializer.Serialize(value, Events);
}
