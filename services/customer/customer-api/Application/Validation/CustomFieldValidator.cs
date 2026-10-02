namespace CustomerApi.Application.Validation;
using System.Globalization;
using System.Text.Json;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Normalization;
using CustomerApi.Domain.Entities;

/// <summary>
/// Validates <c>custom_fields</c> against the active definitions on every create, edit and import
/// (CF-3) and names the offending field in the error (AC-10). Values whose definition has been
/// deactivated are preserved so they reappear on reactivation (CF-5, AC-11).
/// </summary>
public static class CustomFieldValidator
{
    public static readonly string[] SupportedTypes =
    [
        "text", "textarea", "number", "currency", "date", "datetime",
        "boolean", "select", "multiselect", "email", "phone", "url", "user"
    ];

    /// <param name="activeDefinitions">Active definitions for the entity type in this organization.</param>
    /// <param name="incoming">Values from the request; <c>null</c> means the caller sent none.</param>
    /// <param name="existingJson">The record's currently stored <c>custom_fields</c> JSONB.</param>
    /// <param name="isNewRecord">Required fields are enforced on new saves only (CF-4).</param>
    /// <returns>The JSON to store.</returns>
    public static string Validate(
        IReadOnlyCollection<CustomFieldDefinition> activeDefinitions,
        CustomFieldValues? incoming,
        string? existingJson,
        bool isNewRecord)
    {
        var merged = Parse(existingJson);
        var errors = new Dictionary<string, string[]>();
        var byKey = activeDefinitions.ToDictionary(d => d.FieldKey, StringComparer.Ordinal);

        if (incoming is not null)
        {
            foreach (var (key, value) in incoming)
            {
                if (!byKey.TryGetValue(key, out var definition))
                {
                    AddError(errors, key, "There is no active custom field with this key.");
                    continue;
                }

                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    if (definition.IsRequired && isNewRecord)
                        AddError(errors, key, $"'{definition.Label}' is required.");
                    else
                        merged.Remove(key);
                    continue;
                }

                var problem = Check(definition, value);
                if (problem is not null) AddError(errors, key, problem);
                else merged[key] = value.Clone();
            }
        }

        if (isNewRecord)
        {
            foreach (var definition in activeDefinitions.Where(d => d.IsRequired))
            {
                if (!merged.TryGetValue(definition.FieldKey, out var stored)
                    || stored.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    if (!errors.ContainsKey($"customFields.{definition.FieldKey}"))
                        AddError(errors, definition.FieldKey, $"'{definition.Label}' is required.");
                }
            }
        }

        if (errors.Count > 0) throw new CustomerValidationException(errors);

        return JsonSerializer.Serialize(merged);
    }

    /// <summary>Validates the option list supplied when a definition is created or edited (CF-1).</summary>
    public static void ValidateDefinition(string fieldType, IReadOnlyList<string>? options)
    {
        if (!SupportedTypes.Contains(fieldType))
            throw new CustomerValidationException("fieldType",
                $"Unsupported field type. Use one of: {string.Join(", ", SupportedTypes)}.");

        var needsOptions = fieldType is "select" or "multiselect";

        if (needsOptions && (options is null || options.Count == 0))
            throw new CustomerValidationException("options", $"A '{fieldType}' field needs at least one option.");

        if (!needsOptions && options is { Count: > 0 })
            throw new CustomerValidationException("options", $"A '{fieldType}' field does not take options.");

        if (options is not null && options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
            throw new CustomerValidationException("options", "Options must be unique.");
    }

    private static string? Check(CustomFieldDefinition definition, JsonElement value) => definition.FieldType switch
    {
        "text" or "textarea" =>
            value.ValueKind == JsonValueKind.String ? null : "Expected text.",

        "number" or "currency" =>
            value.ValueKind == JsonValueKind.Number ? null : "Expected a number.",

        "boolean" =>
            value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "Expected true or false.",

        "date" => value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out _)
            ? null : "Expected a date as yyyy-MM-dd.",

        "datetime" => value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out _)
            ? null : "Expected an ISO 8601 date and time.",

        "email" => value.ValueKind == JsonValueKind.String && EmailValidator.IsValid(value.GetString())
            ? null : "Expected a valid email address.",

        "phone" => value.ValueKind == JsonValueKind.String && PhoneNormalizer.IsValid(value.GetString())
            ? null : "Expected a valid phone number.",

        "url" => value.ValueKind == JsonValueKind.String
            && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? null : "Expected a URL starting with http:// or https://.",

        "user" => value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _)
            ? null : "Expected a user id.",

        "select" => value.ValueKind == JsonValueKind.String
            ? (Options(definition).Contains(value.GetString()!, StringComparer.Ordinal)
                ? null
                : $"'{value.GetString()}' is not one of the allowed options.")
            : "Expected one of the allowed options.",

        "multiselect" => CheckMultiselect(definition, value),

        _ => $"Unsupported field type '{definition.FieldType}'."
    };

    private static string? CheckMultiselect(CustomFieldDefinition definition, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return "Expected a list of options.";

        var allowed = Options(definition);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return "Expected a list of option names.";
            if (!allowed.Contains(item.GetString()!, StringComparer.Ordinal))
                return $"'{item.GetString()}' is not one of the allowed options.";
        }
        return null;
    }

    private static string[] Options(CustomFieldDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Options)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(definition.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddError(IDictionary<string, string[]> errors, string key, string message)
        => errors[$"customFields.{key}"] = [message];
}
