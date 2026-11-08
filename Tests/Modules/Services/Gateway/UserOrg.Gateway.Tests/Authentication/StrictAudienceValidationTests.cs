using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Authentication;

namespace UserOrg.Gateway.Tests.Authentication;

/// <summary>
/// The strict audience rule of ADR 0121: a token passes only when it carries at least one audience and every
/// one of them is configured, compared ordinally.
/// </summary>
public sealed class StrictAudienceValidationTests
{
    [Fact]
    public void Should_Accept_When_EveryAudienceIsConfigured() =>
        Assert.True(StrictAudienceValidation.AllAudiencesConfigured(["userorg", "b"], ["userorg", "b"]));

    [Fact]
    public void Should_Reject_When_OneAudienceIsNotConfigured() =>
        Assert.False(StrictAudienceValidation.AllAudiencesConfigured(["userorg", "other"], ["userorg"]));

    [Fact]
    public void Should_Reject_When_TheTokenCarriesNoAudience() =>
        Assert.False(StrictAudienceValidation.AllAudiencesConfigured([], ["userorg"]));

    [Fact]
    public void Should_CompareOrdinally() =>
        Assert.False(StrictAudienceValidation.AllAudiencesConfigured(["UserOrg"], ["userorg"]));

    [Fact]
    public void Should_Reject_When_NothingIsConfigured() =>
        Assert.False(StrictAudienceValidation.AllAudiencesConfigured(["userorg"], null));

    [Fact]
    public void Should_ReturnNoValidator_When_NotStrict() =>
        Assert.Null(StrictAudienceValidation.For(false));

    [Fact]
    public void Should_ReturnAValidatorThatRejectsAnUnconfiguredAudience_When_Strict()
    {
        var validator = StrictAudienceValidation.For(true);

        Assert.NotNull(validator);
        Assert.False(validator(
            ["userorg", "x"],
            new JsonWebToken("{}", "{}"),
            new TokenValidationParameters { ValidAudiences = ["userorg"] }));
    }
}
