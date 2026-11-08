using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Project.GetProject;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

namespace Core.UnitTests.Projects;

public sealed class GetProjectEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ProjectDetails Sample() => new(
        ShortName, ProjectName.Create("Project One"), Description.Create("a project"),
        Email.Create("p@example.org"), null, EOperationMode.Editable, EProjectState.Active, ServiceName.Default,
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        DomainTypeName.Create("HPC"), false, [], [], []);

    private static async Task<IResult> InvokeAsync(Caller caller, ProjectDetails? project)
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        return await GetProjectEndpoint.HandleAsync(
            new GetProjectEndpoint.Request(ShortName), caller, query.Object, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var result = await InvokeAsync(CallerOutOfScope(), Sample());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectNotFound()
    {
        var result = await InvokeAsync(CallerInScope(), project: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_MissingOrOutOfScope()
    {
        var outOfScope = await InvokeAsync(CallerOutOfScope(), project: null);
        var missing = await InvokeAsync(CallerInScope(), project: null);

        var hidden = Assert.IsType<ProblemHttpResult>(outOfScope);
        var notThere = Assert.IsType<ProblemHttpResult>(missing);

        // Existence must not leak: scope-hidden and genuinely-missing are indistinguishable (ADR-0018 rider).
        Assert.Equal(StatusCodes.Status404NotFound, hidden.StatusCode);
        Assert.Equal("project.not_found", hidden.ProblemDetails.Extensions["code"]);
        Assert.Equal(hidden.ProblemDetails.Extensions["code"], notThere.ProblemDetails.Extensions["code"]);
        Assert.Equal(hidden.ProblemDetails.Detail, notThere.ProblemDetails.Detail);
        Assert.Equal(hidden.ProblemDetails.Title, notThere.ProblemDetails.Title);
    }

    [Fact]
    public async Task Should_ReturnOk_And_MapProject_When_FoundInScope()
    {
        var result = await InvokeAsync(CallerInScope(), Sample());

        var ok = Assert.IsType<Ok<GetProjectResponse>>(result);
        Assert.Equal("PROJ-1", ok.Value!.ShortName);
        Assert.Equal("Editable", ok.Value.Mode);
        Assert.False(ok.Value.IsDeleted);
    }
}
