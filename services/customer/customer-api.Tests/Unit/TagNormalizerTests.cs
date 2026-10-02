namespace CustomerApi.Tests.Unit;
using CustomerApi.Application.Exceptions;
using CustomerApi.Application.Normalization;
using FluentAssertions;

/// <summary>TAG-2: lower case, trimmed, max 40 chars each, max 20 per record.</summary>
public class TagNormalizerTests
{
    [Fact]
    public void Normalize_TrimsLowerCasesAndDeduplicates()
        => TagNormalizer.Normalize(["  VIP ", "vip", "Enterprise"], 20)
            .Should().Equal("vip", "enterprise");

    [Fact]
    public void Normalize_DropsBlankEntries()
        => TagNormalizer.Normalize(["ok", "   ", ""], 20).Should().Equal("ok");

    [Fact]
    public void Normalize_Null_ReturnsEmptyArray()
        => TagNormalizer.Normalize(null, 20).Should().BeEmpty();

    [Fact]
    public void Normalize_TagLongerThan40Chars_Throws()
    {
        var act = () => TagNormalizer.Normalize([new string('a', 41)], 20);
        act.Should().Throw<CustomerValidationException>()
            .WithMessage("*40*");
    }

    [Fact]
    public void Normalize_MoreThanLimitTags_Throws()
    {
        var many = Enumerable.Range(0, 21).Select(i => $"tag{i}").ToArray();
        var act = () => TagNormalizer.Normalize(many, 20);
        act.Should().Throw<CustomerValidationException>().WithMessage("*20*");
    }
}
