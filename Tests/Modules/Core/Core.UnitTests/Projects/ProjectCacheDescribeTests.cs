using System.Security.Claims;

using Core.Features.Project;
using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;

namespace Core.UnitTests.Projects;

public sealed class ProjectCacheDescribeTests
{
    private static HttpContext Http(object? routeShortName, params Claim[] claims)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };
        if (routeShortName is not null)
            http.Request.RouteValues["projectShortName"] = routeShortName;
        return http;
    }

    private static Claim Sub(string value = "user-1") => new("sub", value);

    private static Claim ProjectRoles(string json) => new("projectRoles", json);

    [Fact]
    public async Task Should_SkipCaching_When_RouteHasNoProjectShortName()
    {
        var descriptor = await ProjectCache.Describe(Http(routeShortName: null, Sub()), CancellationToken.None);

        Assert.Null(descriptor);
    }

    [Fact]
    public async Task Should_VaryInScope_And_TagByProject_When_CallerHoldsTheProject()
    {
        var http = Http("PROJ-1", Sub(), ProjectRoles("""{"Owner":["PROJ-1"]}"""));

        var descriptor = await ProjectCache.Describe(http, CancellationToken.None);

        Assert.NotNull(descriptor);
        Assert.Equal(TimeSpan.FromMinutes(5), descriptor.Expiration);
        Assert.Equal("1", descriptor.VaryByValues["inScope"]);
        Assert.Equal([CacheTags.Project("PROJ-1")], descriptor.Tags);
    }

    [Fact]
    public async Task Should_VaryOutOfScope_When_CallerDoesNotHoldTheProject()
    {
        var http = Http("PROJ-1", Sub(), ProjectRoles("""{"Owner":["OTHER-1"]}"""));

        var descriptor = await ProjectCache.Describe(http, CancellationToken.None);

        Assert.Equal("0", descriptor!.VaryByValues["inScope"]);
        Assert.Equal([CacheTags.Project("PROJ-1")], descriptor.Tags);
    }

    [Fact]
    public async Task Should_VaryOutOfScope_When_CallerIsAnonymous()
    {
        var descriptor = await ProjectCache.Describe(Http("PROJ-1"), CancellationToken.None);

        Assert.Equal("0", descriptor!.VaryByValues["inScope"]);
    }

    [Fact]
    public async Task Should_VaryOutOfScope_And_TagByTheRawRouteValue_When_ShortNameIsUnparseable()
    {
        var http = Http(" ", Sub(), ProjectRoles("""{"Owner":["PROJ-1"]}"""));

        var descriptor = await ProjectCache.Describe(http, CancellationToken.None);

        Assert.Equal("0", descriptor!.VaryByValues["inScope"]);
        Assert.Equal([CacheTags.Project(" ")], descriptor.Tags);
    }

    [Fact]
    public async Task Should_ShareOneCacheEntry_When_TwoDifferentUsersHoldTheProject()
    {
        var first = await ProjectCache.Describe(
            Http("PROJ-1", Sub("user-1"), ProjectRoles("""{"Owner":["PROJ-1"]}""")), CancellationToken.None);
        var second = await ProjectCache.Describe(
            Http("PROJ-1", Sub("user-2"), ProjectRoles("""{"Owner":["PROJ-1"]}""")), CancellationToken.None);

        // Sharing is correct: the GetProject response does not depend on the caller.
        Assert.Equal(first!.VaryByValues, second!.VaryByValues);
    }
}
