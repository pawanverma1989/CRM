namespace CustomerApi.Application.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Distinguishes "field absent from the PATCH body" from "field explicitly set to null".
/// Without it a PATCH could not clear an optional field (COM-3, CON-6).
/// </summary>
[JsonConverter(typeof(OptionalConverterFactory))]
public readonly struct Optional<T>
{
    public Optional(T? value)
    {
        HasValue = true;
        Value = value;
    }

    /// <summary>True when the property was present in the request body.</summary>
    public bool HasValue { get; }

    public T? Value { get; }

    /// <summary>Returns <paramref name="current"/> unchanged when the property was absent.</summary>
    public T? Or(T? current) => HasValue ? Value : current;

    public static implicit operator Optional<T>(T? value) => new(value);
}

public sealed class OptionalConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(
            typeof(OptionalConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

    private sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        // Read is only reached when the property is present, which is exactly what HasValue means.
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (!value.HasValue) { writer.WriteNullValue(); return; }
            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}
