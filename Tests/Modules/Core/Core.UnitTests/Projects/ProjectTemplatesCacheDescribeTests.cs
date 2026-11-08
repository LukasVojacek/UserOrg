using System.Security.Claims;

using Core.Features.Project.GetProjectTemplates;

using Microsoft.AspNetCore.Http;

namespace Core.UnitTests.Projects;

public sealed class ProjectTemplatesCacheDescribeTests
{
    private static HttpContext Http(string subject)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", subject), new Claim("projectRoles", """{"Owner":["PROJ-1"]}""")], "test")),
        };
        http.Request.RouteValues["projectShortName"] = "PROJ-1";
        return http;
    }

    [Fact]
    public async Task Should_VaryBySubject_When_TwoUsersHoldTheProject()
    {
        var first = await ProjectTemplatesCache.Describe(Http("user-1"), TestContext.Current.CancellationToken);
        var second = await ProjectTemplatesCache.Describe(Http("user-2"), TestContext.Current.CancellationToken);

        Assert.NotEqual(first!.VaryByValues, second!.VaryByValues);
    }

    [Fact]
    public async Task Should_ShareEntry_When_SameUserCallsTwice()
    {
        var first = await ProjectTemplatesCache.Describe(Http("user-1"), TestContext.Current.CancellationToken);
        var second = await ProjectTemplatesCache.Describe(Http("user-1"), TestContext.Current.CancellationToken);

        Assert.Equal(first!.VaryByValues, second!.VaryByValues);
    }
}