namespace CustomerApi.Tests.Unit;
using CustomerApi.Application.Normalization;
using FluentAssertions;

/// <summary>COM-4 / AC-4: domain stored lower-case without scheme, www., path or port.</summary>
public class DomainNormalizerTests
{
    [Theory]
    [InlineData("https://www.Acme.com/", "acme.com")]
    [InlineData("http://ACME.COM", "acme.com")]
    [InlineData("www.acme.com", "acme.com")]
    [InlineData("acme.com", "acme.com")]
    [InlineData("https://acme.com:8443/about/us?x=1", "acme.com")]
    [InlineData("  Acme.CO.in.  ", "acme.co.in")]
    [InlineData("https://sub.acme.com/", "sub.acme.com")]
    public void Normalize_VariousForms_ReturnsBareLowerCaseDomain(string input, string expected)
        => DomainNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankInput_ReturnsNull(string? input)
        => DomainNormalizer.Normalize(input).Should().BeNull();

    [Theory]
    [InlineData("acme.com", true)]
    [InlineData("a.io", true)]
    [InlineData("sub.domain.co.in", true)]
    [InlineData("acme", false)]
    [InlineData("acme..com", false)]
    [InlineData("-acme.com", false)]
    [InlineData("acme.c", false)]
    [InlineData("acme.com/path", false)]
    [InlineData("acme com", false)]
    public void IsValid_ChecksPlausibleDomain(string value, bool expected)
        => DomainNormalizer.IsValid(value).Should().Be(expected);

    [Fact]
    public void Normalize_AC4_TwoSpellingsOfTheSameCompanyCollide()
        => DomainNormalizer.Normalize("https://www.Acme.com/")
            .Should().Be(DomainNormalizer.Normalize("acme.com"));
}
