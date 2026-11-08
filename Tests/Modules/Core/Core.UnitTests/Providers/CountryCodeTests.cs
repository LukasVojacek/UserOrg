using Core.Domain.Provider;

namespace Core.UnitTests.Providers;

public sealed class CountryCodeTests
{
    [Theory]
    [InlineData("CZ", "CZ")]
    [InlineData("cz", "CZ")]
    [InlineData("De", "DE")]
    public void Should_ReturnTheUpperCaseCode_When_ValueIsTwoLetters(string value, string expected)
    {
        Assert.Equal(expected, CountryCode.Create(value).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_ThrowEmpty_When_ValueIsNullEmptyOrWhitespace(string? value)
    {
        var ex = Assert.Throws<ArgumentException>(() => CountryCode.Create(value!));

        Assert.Equal("value", ex.ParamName);
        Assert.StartsWith("CountryCode cannot be empty", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("CZE")]
    [InlineData(" cz")]
    [InlineData("C1")]
    [InlineData("c ")]
    public void Should_ThrowNotIso_When_ValueIsNotExactlyTwoLetters(string value)
    {
        var ex = Assert.Throws<ArgumentException>(() => CountryCode.Create(value));

        Assert.Equal("value", ex.ParamName);
        Assert.StartsWith("CountryCode must be a 2-letter ISO code", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_ReturnNone_When_DatabaseValueIsNull()
    {
        Assert.Equal(CountryCode.None, CountryCode.From(null));
    }

    [Fact]
    public void Should_Normalise_When_DatabaseValueIsSet()
    {
        Assert.Equal("CZ", CountryCode.From("cz").Value);
    }
}
