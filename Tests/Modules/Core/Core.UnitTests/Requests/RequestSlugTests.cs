using Core.Domain.Request;

namespace Core.UnitTests.Requests;

public sealed class RequestSlugTests
{
    [Fact]
    public void Should_BuildSlug_FromEmailPrefixAndProjectShortName()
    {
        var slug = RequestSlug.New(Email.Create("lukas.drabek@vs.com"), ProjectShortName.Create("testproject"));

        Assert.StartsWith("req-luk-testproject-", slug);
        var r6 = slug["req-luk-testproject-".Length..];
        Assert.Matches("^[a-z0-9]{6}$", r6);
    }

    [Fact]
    public void Should_DifferOnRepeat_ForSameRequesterAndProject()
    {
        var email = Email.Create("lukas.drabek@vs.com");
        var project = ProjectShortName.Create("testproject");

        // The r6 suffix exists precisely so a second request from the same requester+project is addressable.
        Assert.NotEqual(RequestSlug.New(email, project), RequestSlug.New(email, project));
    }
}
