namespace CustomerApi.Tests.Unit;
using System.Text.Json;
using CustomerApi.Application.DTOs;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Validation;
using CustomerApi.Domain.Entities;
using FluentAssertions;

/// <summary>CF-3..CF-6, AC-10, AC-11.</summary>
public class CustomFieldValidatorTests
{
    private static CustomFieldDefinition Def(
        string key, string type, bool required = false, string[]? options = null, bool active = true) => new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            EntityType = "contact",
            FieldKey = key,
            Label = key,
            FieldType = type,
            IsRequired = required,
            IsActive = active,
            Options = options is null ? null : JsonSerializer.Serialize(options)
        };

    private static CustomFieldValues Values(string json)
        => new(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);

    [Fact]
    public void Validate_AC10_SelectValueNotInOptions_RejectedNamingTheField()
    {
        var defs = new[] { Def("region", "select", required: true, options: ["north", "south"]) };

        var act = () => CustomFieldValidator.Validate(defs, Values("""{"region":"east"}"""), "{}", isNewRecord: true);

        act.Should().Throw<CustomerValidationException>()
            .Which.Errors.Should().ContainKey("customFields.region");
    }

    [Fact]
    public void Validate_SelectValueInOptions_Accepted()
    {
        var defs = new[] { Def("region", "select", options: ["north", "south"]) };

        var json = CustomFieldValidator.Validate(defs, Values("""{"region":"north"}"""), "{}", isNewRecord: true);

        json.Should().Contain("north");
    }

    [Fact]
    public void Validate_RequiredFieldMissingOnNewRecord_Rejected()
    {
        var defs = new[] { Def("region", "select", required: true, options: ["north"]) };

        var act = () => CustomFieldValidator.Validate(defs, null, "{}", isNewRecord: true);

        act.Should().Throw<CustomerValidationException>()
            .Which.Errors.Should().ContainKey("customFields.region");
    }

    [Fact]
    public void Validate_CF4_RequiredFieldMissingOnExistingRecord_DoesNotBlockUnrelatedEdit()
    {
        var defs = new[] { Def("region", "select", required: true, options: ["north"]), Def("score", "number") };

        var json = CustomFieldValidator.Validate(defs, Values("""{"score":7}"""), "{}", isNewRecord: false);

        json.Should().Contain("score");
    }

    [Fact]
    public void Validate_CF5_ValuesOfDeactivatedFieldsArePreserved()
    {
        // "legacy" has no active definition any more; its stored value must survive the save
        // and come back when the field is reactivated (AC-11).
        var defs = new[] { Def("score", "number") };

        var json = CustomFieldValidator.Validate(
            defs, Values("""{"score":3}"""), """{"legacy":"keep me","score":1}""", isNewRecord: false);

        var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        result["legacy"].GetString().Should().Be("keep me");
        result["score"].GetInt32().Should().Be(3);
    }

    [Fact]
    public void Validate_UnknownFieldKey_Rejected()
    {
        var defs = new[] { Def("score", "number") };

        var act = () => CustomFieldValidator.Validate(defs, Values("""{"nope":1}"""), "{}", isNewRecord: true);

        act.Should().Throw<CustomerValidationException>()
            .Which.Errors.Should().ContainKey("customFields.nope");
    }

    [Theory]
    [InlineData("number", "\"abc\"")]
    [InlineData("boolean", "\"yes\"")]
    [InlineData("date", "\"31-12-2026\"")]
    [InlineData("datetime", "\"not a time\"")]
    [InlineData("email", "\"bad\"")]
    [InlineData("phone", "\"12345\"")]
    [InlineData("url", "\"acme.com\"")]
    [InlineData("user", "\"not-a-guid\"")]
    [InlineData("currency", "\"free\"")]
    [InlineData("text", "42")]
    public void Validate_WrongTypeForFieldType_Rejected(string fieldType, string valueJson)
    {
        var defs = new[] { Def("f", fieldType) };

        var act = () => CustomFieldValidator.Validate(defs, Values($"{{\"f\":{valueJson}}}"), "{}", isNewRecord: true);

        act.Should().Throw<CustomerValidationException>()
            .Which.Errors.Should().ContainKey("customFields.f");
    }

    [Theory]
    [InlineData("number", "42")]
    [InlineData("currency", "1999.50")]
    [InlineData("boolean", "true")]
    [InlineData("date", "\"2026-12-31\"")]
    [InlineData("datetime", "\"2026-12-31T10:00:00Z\"")]
    [InlineData("email", "\"a@b.com\"")]
    [InlineData("phone", "\"9876543210\"")]
    [InlineData("url", "\"https://acme.com\"")]
    [InlineData("text", "\"hello\"")]
    [InlineData("textarea", "\"hello\"")]
    public void Validate_CorrectTypeForFieldType_Accepted(string fieldType, string valueJson)
    {
        var defs = new[] { Def("f", fieldType) };

        var act = () => CustomFieldValidator.Validate(defs, Values($"{{\"f\":{valueJson}}}"), "{}", isNewRecord: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MultiselectWithOneBadOption_Rejected()
    {
        var defs = new[] { Def("f", "multiselect", options: ["a", "b"]) };

        var act = () => CustomFieldValidator.Validate(defs, Values("""{"f":["a","z"]}"""), "{}", isNewRecord: true);

        act.Should().Throw<CustomerValidationException>();
    }

    [Fact]
    public void Validate_ExplicitNullClearsAnOptionalValue()
    {
        var defs = new[] { Def("score", "number") };

        var json = CustomFieldValidator.Validate(defs, Values("""{"score":null}"""), """{"score":5}""", isNewRecord: false);

        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!
            .Should().NotContainKey("score");
    }

    [Fact]
    public void Validate_NoDefinitionsAndNoValues_ReturnsEmptyObject()
        => CustomFieldValidator.Validate([], null, "{}", isNewRecord: true).Should().Be("{}");
}
