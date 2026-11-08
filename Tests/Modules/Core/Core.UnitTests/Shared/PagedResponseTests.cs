using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Core.UnitTests.Shared;

public sealed class PagedResponseTests
{
    private static DefaultHttpContext Context(string path = "/api/v2/Projects", string query = "")
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        return http;
    }

    [Fact]
    public void Should_ReturnBareArrayBody_And_SetTotalCount_When_OffsetPaged()
    {
        var http = Context();

        var result = PagedResponse.Offset(http, new[] { "a", "b" }, page: 1, limit: 50, totalItems: 2);

        var ok = Assert.IsType<Ok<IReadOnlyList<string>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("2", http.Response.Headers["X-Total-Count"]);
    }

    [Fact]
    public void Should_EmitAllRels_When_MiddlePage()
    {
        var http = Context();

        PagedResponse.Offset(http, new[] { "a" }, page: 2, limit: 10, totalItems: 25);

        var link = http.Response.Headers.Link.ToString();
        Assert.Contains("rel=\"first\"", link);
        Assert.Contains("rel=\"prev\"", link);
        Assert.Contains("rel=\"next\"", link);
        Assert.Contains("rel=\"last\"", link);
        Assert.Contains("https://api.test/api/v2/Projects?page=1&limit=10", link); // first & prev
        Assert.Contains("page=3&limit=10", link);                                  // next
        Assert.Contains("https://api.test/api/v2/Projects?page=3&limit=10>; rel=\"last\"", link);
    }

    [Fact]
    public void Should_OmitPrevAndFirst_When_FirstPage()
    {
        var http = Context();

        PagedResponse.Offset(http, new[] { "a" }, page: 1, limit: 10, totalItems: 25);

        var link = http.Response.Headers.Link.ToString();
        Assert.DoesNotContain("rel=\"prev\"", link);
        Assert.DoesNotContain("rel=\"first\"", link);
        Assert.Contains("rel=\"next\"", link);
        Assert.Contains("rel=\"last\"", link);
    }

    [Fact]
    public void Should_OmitNextAndLast_When_LastPage()
    {
        var http = Context();

        PagedResponse.Offset(http, new[] { "a" }, page: 3, limit: 10, totalItems: 25);

        var link = http.Response.Headers.Link.ToString();
        Assert.Contains("rel=\"first\"", link);
        Assert.Contains("rel=\"prev\"", link);
        Assert.DoesNotContain("rel=\"next\"", link);
        Assert.DoesNotContain("rel=\"last\"", link);
    }

    [Fact]
    public void Should_OmitLinkHeader_When_Empty()
    {
        var http = Context();

        PagedResponse.Offset(http, Array.Empty<string>(), page: 1, limit: 10, totalItems: 0);

        Assert.Equal("0", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }

    [Fact]
    public void Should_GiveEachRelItsOwnPage_And_KeepEveryOtherParam_When_TheQueryRepeatsAndCasesKeys()
    {
        var http = Context(query: "?Page=2&domainId=5&tag=a&tag=b&limit=10");

        PagedResponse.Offset(http, new[] { "a" }, page: 2, limit: 10, totalItems: 35);

        Assert.Equal(
            "<https://api.test/api/v2/Projects?Page=1&domainId=5&tag=a&tag=b&limit=10>; rel=\"first\", "
            + "<https://api.test/api/v2/Projects?Page=1&domainId=5&tag=a&tag=b&limit=10>; rel=\"prev\", "
            + "<https://api.test/api/v2/Projects?Page=3&domainId=5&tag=a&tag=b&limit=10>; rel=\"next\", "
            + "<https://api.test/api/v2/Projects?Page=4&domainId=5&tag=a&tag=b&limit=10>; rel=\"last\"",
            http.Response.Headers.Link.ToString());
    }

    [Fact]
    public void Should_PreserveOtherQueryParams_When_BuildingLinks()
    {
        var http = Context(query: "?domainId=5&page=2&limit=10");

        PagedResponse.Offset(http, new[] { "a" }, page: 2, limit: 10, totalItems: 25);

        var link = http.Response.Headers.Link.ToString();
        Assert.Contains("domainId=5", link);
        Assert.Contains("page=3", link); // next still carries the filter
    }

    [Fact]
    public void Should_ReturnBareArray_And_TotalCount_WithoutLink_When_Complete()
    {
        var http = Context();

        var result = PagedResponse.Complete(http, new[] { "x", "y", "z" });

        var ok = Assert.IsType<Ok<IReadOnlyList<string>>>(result);
        Assert.Equal(3, ok.Value!.Count);
        Assert.Equal("3", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }
}
