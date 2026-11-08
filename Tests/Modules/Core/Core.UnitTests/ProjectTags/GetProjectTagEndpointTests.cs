using Core.DataAccess.Queries.Contracts;
using Core.Features.ProjectTag.GetProjectTag;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

namespace Core.UnitTests.ProjectTags;

public sealed class GetProjectTagEndpointTests
{
    private static async Task<IResult> InvokeAsync(string tagName, ProjectTagSummary? tag)
    {
        var query = new Mock<IProjectTagQuery>();
        query.Setup(q => q.GetProjectTagByNameAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tag);

        return await GetProjectTagEndpoint.HandleAsync(new GetProjectTagEndpoint.Request(tagName), query.Object, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_TagNameInvalid()
    {
        var query = new Mock<IProjectTagQuery>();

        var result = await GetProjectTagEndpoint.HandleAsync(new GetProjectTagEndpoint.Request("   "), query.Object, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("projecttag.invalid_name", problem.ProblemDetails.Extensions["code"]);
        query.Verify(q => q.GetProjectTagByNameAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNotFoundProblem_WithCode_When_TagNotFound()
    {
        var result = await InvokeAsync("hpc", tag: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("projecttag.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnOk_And_MapTag_When_FoundByName()
    {
        var result = await InvokeAsync("hpc", new ProjectTagSummary("hpc"));

        var ok = Assert.IsType<Ok<GetProjectTagEndpoint.Response>>(result);
        Assert.Equal("hpc", ok.Value!.Name);
    }
}
