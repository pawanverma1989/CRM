namespace CustomerApi.Tests.Unit;
using CustomerApi.Application.Normalization;
using FluentAssertions;

/// <summary>CON-4 / AC-5: E.164 with +91 assumed when no country code is given.</summary>
public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("+91-98765-43210", "+919876543210")]
    [InlineData("098765 43210", "+919876543210")]
    [InlineData("0091 98765 43210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("(+91) 98765.43210", "+919876543210")]
    public void Normalize_IndianNumbers_ReturnsSameE164(string input, string expected)
        => PhoneNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("+1 415 555 2671", "+14155552671")]
    [InlineData("+44 20 7183 8750", "+442071838750")]
    public void Normalize_InternationalNumbers_KeepsCountryCode(string input, string expected)
        => PhoneNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("12345")]
    public void Normalize_BlankOrImplausible_ReturnsNull(string? input)
        => PhoneNormalizer.Normalize(input).Should().BeNull();

    [Fact]
    public void Normalize_AC5_LocalAndInternationalSpellingMatch()
        => PhoneNormalizer.Normalize("9876543210")
            .Should().Be(PhoneNormalizer.Normalize("+91 98765 43210"));

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("+91 98765 43210", true)]
    [InlineData("12345", false)]
    [InlineData("not a phone", false)]
    public void IsValid_ChecksPlausiblePhone(string value, bool expected)
        => PhoneNormalizer.IsValid(value).Should().Be(expected);
}
