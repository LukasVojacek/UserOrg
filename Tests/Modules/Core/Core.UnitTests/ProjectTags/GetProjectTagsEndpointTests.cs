using Core.DataAccess.Queries.Contracts;
using Core.Features.ProjectTag.GetProjectTags;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.ProjectTags;

public sealed class GetProjectTagsEndpointTests
{
    private static ProjectTagSummary Sample(string name) => new(name);

    private static DefaultHttpContext HttpContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = "/api/v2/ProjectTags";
        return http;
    }

    private static async Task<IResult> InvokeAsync(
        string? tagName = null,
        List<ProjectTagSummary>? summaries = null,
        int? totalItems = null,
        PageRequest? page = null,
        Action<ProjectTagFilter>? captureFilter = null)
    {
        var items = summaries ?? [];
        var query = new Mock<IProjectTagQuery>();
        query.Setup(q => q.GetProjectTagsAsync(It.IsAny<ProjectTagFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectTagFilter, int, int, CancellationToken>((f, _, _, _) => captureFilter?.Invoke(f))
            .ReturnsAsync(new QueryPage<ProjectTagSummary>(items, totalItems ?? items.Count));

        return await GetProjectTagsEndpoint.HandleAsync(
            HttpContext(), tagName, page ?? new PageRequest(null, null), query.Object, Pagination(), CancellationToken.None);
    }

    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    [Fact]
    public async Task Should_PassTagNameToFilter()
    {
        ProjectTagFilter? captured = null;

        await InvokeAsync(tagName: "hpc", captureFilter: f => captured = f);

        Assert.NotNull(captured);
        Assert.Equal("hpc", captured!.TagName);
    }

    [Fact]
    public async Task Should_ReturnEmptyBareArray_When_NoMatches()
    {
        var result = await InvokeAsync();

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTagsEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
    }

    [Fact]
    public async Task Should_MapSummaryToResponse_When_ResultsExist()
    {
        var result = await InvokeAsync(summaries: [Sample("hpc")]);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTagsEndpoint.Response>>>(result);
        var item = Assert.Single(ok.Value!);
        Assert.Equal("hpc", item.Name);
    }

    [Fact]
    public async Task Should_SetTotalCount_AndNextLink_When_MoreResultsExist()
    {
        var http = HttpContext();
        var query = new Mock<IProjectTagQuery>();
        query.Setup(q => q.GetProjectTagsAsync(It.IsAny<ProjectTagFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProjectTagSummary>([Sample("a"), Sample("b")], 5));

        var result = await GetProjectTagsEndpoint.HandleAsync(
            http, null, new PageRequest(1, 2), query.Object, Pagination(), CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyList<GetProjectTagsEndpoint.Response>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.Contains("rel=\"next\"", http.Response.Headers.Link.ToString());
    }

    [Fact]
    public async Task Should_CapLimit_When_LimitExceedsMax()
    {
        int? capturedLimit = null;
        var query = new Mock<IProjectTagQuery>();
        query.Setup(q => q.GetProjectTagsAsync(It.IsAny<ProjectTagFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ProjectTagFilter, int, int, CancellationToken>((_, _, limit, _) => capturedLimit = limit)
            .ReturnsAsync(new QueryPage<ProjectTagSummary>([], 0));

        await GetProjectTagsEndpoint.HandleAsync(
            HttpContext(), null, new PageRequest(1, PageRequest.MaxLimit + 1000), query.Object, Pagination(), CancellationToken.None);

        Assert.Equal(PageRequest.MaxLimit, capturedLimit);
    }
}
