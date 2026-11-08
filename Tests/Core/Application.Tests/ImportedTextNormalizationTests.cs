using UserOrg.Shared.Extensions;

using UserOrganizationManagement.Domain.ValueObjects;

public sealed class ImportedTextNormalizationTests
{
    [Fact]
    public void RemoveControlCharacters_WhenTextContainsControlOrFormatCharacters_RemovesThem()
    {
        Assert.Equal("KarolinaHPC", "Karolina\u200bH\u0001PC".RemoveControlCharacters());
    }

    [Fact]
    public void RemoveControlCharacters_WhenTextIsPrintable_ReturnsTheSameInstance()
    {
        const string text = "IT4Innovations – Karolina (Ostravská)";

        Assert.Same(text, text.RemoveControlCharacters());
    }

    [Fact]
    public void CreateNormalized_WhenSlugIsValid_KeepsItUnchanged()
    {
        Assert.Equal("open-24-59", ProjectShortName.CreateNormalized("open-24-59").Value);
    }

    [Fact]
    public void CreateNormalized_WhenSlugCarriesDiacriticsOrSeparators_FoldsThemToAscii()
    {
        Assert.Equal("Ostravska24", ProjectShortName.CreateNormalized("Ostravská 24–").Value);
    }

    [Fact]
    public void CreateNormalized_WhenSlugExceedsTheColumnLength_TruncatesIt()
    {
        var value = ProjectShortName.CreateNormalized(new string('a', 60)).Value;

        Assert.Equal(50, value.Length);
    }

    [Theory]
    [InlineData("open-24-59", true)]
    [InlineData("Ostravská", false)]
    [InlineData("open 24", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_ReflectsTheShortNameRule(string? value, bool expected)
    {
        Assert.Equal(expected, ProjectShortName.IsValid(value));
    }
}
