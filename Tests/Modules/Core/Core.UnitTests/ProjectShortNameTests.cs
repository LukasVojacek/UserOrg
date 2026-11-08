namespace Core.UnitTests;

public sealed class ProjectShortNameTests
{
    [Theory]
    [InlineData("va")]
    [InlineData("valid-Name_123")]
    public void Should_ReturnTrue_When_ValueIsWithinCharsetLengthAndAtLeastTwoCharacters(string value)
    {
        Assert.True(ProjectShortName.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("héllo")]
    public void Should_ReturnFalse_When_ValueIsTooShortOrOutsideCharset(string? value)
    {
        Assert.False(ProjectShortName.IsValid(value));
    }

    [Fact]
    public void Should_ReturnFalse_When_ValueExceedsFiftyCharacters()
    {
        var tooLong = new string('a', 51);

        Assert.False(ProjectShortName.IsValid(tooLong));
    }

    [Fact]
    public void Should_ReturnTrue_When_ValueIsExactlyFiftyCharacters()
    {
        var maxLength = new string('a', 50);

        Assert.True(ProjectShortName.IsValid(maxLength));
    }

    [Fact]
    public void Should_Throw_When_CreatingFromOneCharacterValue()
    {
        Assert.Throws<ArgumentException>(() => ProjectShortName.Create("a"));
    }

    [Fact]
    public void Should_Throw_When_CreatingNormalizedFromOneCharacterValue()
    {
        Assert.Throws<ArgumentException>(() => ProjectShortName.CreateNormalized("a"));
    }

    [Fact]
    public void Should_FoldDiacriticsThenDelegateToCreate_When_CreatingNormalizedFromAccentedValue()
    {
        var result = ProjectShortName.CreateNormalized("héllo wörld!");

        Assert.Equal("helloworld", result.Value);
    }
}
