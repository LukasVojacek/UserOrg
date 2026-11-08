using Core.Domain.OnboardingRequest;

namespace Core.UnitTests;

public sealed class OnboardingRequestSlugTests
{
    [Fact]
    public void Should_BuildSlug_FromEmailPrefixAndProjectShortName()
    {
        var slug = OnboardingRequestSlug.New(Email.Create("lukas.drabek@vs.com"), ProjectShortName.Create("testproject"));

        Assert.StartsWith("req-luk-testproject-", slug.Value);
        var r6 = slug.Value["req-luk-testproject-".Length..];
        Assert.Matches("^[a-z0-9]{6}$", r6);
    }

    [Fact]
    public void Should_DifferOnRepeat_ForSameRequesterAndProject()
    {
        var email = Email.Create("lukas.drabek@vs.com");
        var project = ProjectShortName.Create("testproject");

        Assert.NotEqual(
            OnboardingRequestSlug.New(email, project).Value,
            OnboardingRequestSlug.New(email, project).Value);
    }
}
