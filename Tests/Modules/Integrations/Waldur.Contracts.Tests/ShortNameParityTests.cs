using System.Text;

using UserOrg.Shared.Persistance;

using ByteIdenticalProjectShortName = UserOrganizationManagement.Domain.ValueObjects.ProjectShortName;
using ModularProjectShortName = Core.Domain.Project.ProjectShortName;

namespace Waldur.Contracts.Tests;

/// <summary>
/// Drift guard: three independent short-name rule implementations
/// (<see cref="Waldur.Contracts.ShortNameRules"/>, the byte-identical
/// <see cref="ByteIdenticalProjectShortName"/> value object and the
/// <see cref="ModularProjectShortName"/> value object) must agree on charset and the 50-character
/// maximum. 1-character inputs are deliberately excluded from the shared agreement table: the
/// modular value object's own min-2 rule makes unanimous agreement there unsatisfiable by design.
/// That single deliberate divergence is pinned separately by
/// <see cref="Should_DivergeDeliberately_When_LengthIsOneAndOnlyTheModularRuleRejects"/>, so
/// dropping the min-2 rule from either side fails this suite rather than passing silently.
/// </summary>
public sealed class ShortNameParityTests
{
    [Theory]
    [InlineData("AlphaProject9", true)]
    [InlineData("alpha-Project_9", true)]
    [InlineData("AZaz09-_", true)]
    [InlineData("caf\u00e9", false)]
    [InlineData("alpha\u0001beta", false)]
    [InlineData("alpha\u200bbeta", false)]
    [InlineData("alpha beta", false)]
    [InlineData("alpha.beta", false)]
    [InlineData("alpha^beta", false)]
    [InlineData("alpha`beta", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Should_AgreeOnVerdict_When_ComparingAllThreeImplementations(string? value, bool expected)
        => AssertAllImplementationsAgree(value, expected);

    [Theory]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void Should_AgreeOnVerdict_When_LengthIsAtTheMaximumBoundary(int length, bool expected)
        => AssertAllImplementationsAgree(new string('a', length), expected);

    [Fact]
    public void Should_AgreeOnEveryAsciiCharacter_When_SweepingTheFullRange()
    {
        const string ExpectedAcceptedCharacters =
            "-0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcdefghijklmnopqrstuvwxyz";

        var divergences = new List<string>();
        var accepted = new StringBuilder();

        for (var codePoint = 0; codePoint <= 0x7F; codePoint++)
        {
            var character = (char)codePoint;
            var value = $"a{character}b";

            var contracts = value.IsValidShortName();
            var byteIdentical = ByteIdenticalProjectShortName.IsValid(value);
            var modular = ModularProjectShortName.IsValid(value);

            if (contracts != byteIdentical || contracts != modular)
            {
                divergences.Add(
                    $"U+{codePoint:X4}: contracts={contracts}, byteIdentical={byteIdentical}, modular={modular}");
            }

            if (contracts)
            {
                accepted.Append(character);
            }
        }

        Assert.True(divergences.Count == 0, string.Join("; ", divergences));
        Assert.Equal(ExpectedAcceptedCharacters, accepted.ToString());
    }

    [Fact]
    public void Should_DivergeDeliberately_When_LengthIsOneAndOnlyTheModularRuleRejects()
    {
        const string singleCharacter = "a";

        Assert.True(singleCharacter.IsValidShortName());
        Assert.True(ByteIdenticalProjectShortName.IsValid(singleCharacter));
        Assert.False(ModularProjectShortName.IsValid(singleCharacter));
    }

    [Fact]
    public void Should_EqualDbConstants_When_ComparingShortNameRulesMaxLength()
    {
        Assert.Equal(DbConstants.ProjectShortNameMaxLength, ShortNameRules.MaxLength);
    }

    private static void AssertAllImplementationsAgree(string? value, bool expected)
    {
        Assert.Equal(expected, value.IsValidShortName());
        Assert.Equal(expected, ByteIdenticalProjectShortName.IsValid(value));
        Assert.Equal(expected, ModularProjectShortName.IsValid(value));
    }
}
