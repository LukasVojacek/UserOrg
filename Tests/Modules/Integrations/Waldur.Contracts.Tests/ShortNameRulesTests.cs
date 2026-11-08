namespace Waldur.Contracts.Tests;

public sealed class ShortNameRulesTests
{
    [Fact]
    public void Should_StripControlCharacters_When_TextContainsThem()
    {
        var dirty = "alpha \u0001beta\u200bgamma";

        Assert.Equal("alpha betagamma", dirty.RemoveControlCharacters());
    }

    [Fact]
    public void Should_ReturnSameInstance_When_TextHasNoControlCharacters()
    {
        var clean = "Project Alpha";

        Assert.Same(clean, clean.RemoveControlCharacters());
    }

    [Fact]
    public void Should_AcceptValidCharset_When_ValueIsAsciiLettersDigitsDashUnderscore()
    {
        Assert.True("alpha-Project_9".IsValidShortName());
    }

    [Theory]
    [InlineData("caf\u00e9")]
    [InlineData("alpha beta")]
    [InlineData("alpha.beta")]
    public void Should_RejectValue_When_ValueContainsDisallowedCharacters(string value)
    {
        Assert.False(value.IsValidShortName());
    }

    [Fact]
    public void Should_RejectValue_When_ValueIsNullOrEmpty()
    {
        Assert.False(((string?)null).IsValidShortName());
        Assert.False(string.Empty.IsValidShortName());
    }

    [Fact]
    public void Should_AcceptValue_When_LengthIsAtMaximum()
    {
        var value = new string('a', ShortNameRules.MaxLength);

        Assert.True(value.IsValidShortName());
    }

    [Fact]
    public void Should_RejectValue_When_LengthExceedsMaximum()
    {
        var value = new string('a', ShortNameRules.MaxLength + 1);

        Assert.False(value.IsValidShortName());
    }
}
