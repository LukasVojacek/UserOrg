using UserOrganizationManagement.Domain.ValueObjects;

public sealed class ProjectShortNameTests
{
    [Theory]
    [InlineData("valid-Name_123")]
    [InlineData("a")]
    public void Should_ReturnTrue_When_ValueIsWithinCharsetAndLength(string value)
    {
        Assert.True(ProjectShortName.IsValid(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has.dot")]
    [InlineData("héllo")]
    public void Should_ReturnFalse_When_ValueIsEmptyOrOutsideCharset(string? value)
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
    public void Should_ReturnSameInstance_When_CreatingNormalizedFromAlreadyValidValue()
    {
        const string value = "already-valid_123";

        var result = ProjectShortName.CreateNormalized(value);

        Assert.Same(value, result.Value);
    }

    [Fact]
    public void Should_FoldDiacritics_When_CreatingNormalizedFromAccentedValue()
    {
        var result = ProjectShortName.CreateNormalized("héllo wörld!");

        Assert.Equal("helloworld", result.Value);
    }

    [Fact]
    public void Should_TruncateToFiftyCharacters_When_CreatingNormalizedFromOverlongValue()
    {
        var overlong = new string('a', 60);

        var result = ProjectShortName.CreateNormalized(overlong);

        Assert.Equal(new string('a', 50), result.Value);
    }
}
