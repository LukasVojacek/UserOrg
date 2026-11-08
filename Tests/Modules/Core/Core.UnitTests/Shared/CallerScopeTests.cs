using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Core.UnitTests.Shared;

public sealed class CallerScopeTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    [Fact]
    public void Should_ReturnNull_When_ShortNameInScope()
    {
        var result = CallerInScope().RequireProjectScope(ShortName);

        Assert.Null(result);
    }

    [Fact]
    public void Should_ReturnProjectNotFoundProblem_When_ShortNameOutOfScope()
    {
        var result = CallerOutOfScope().RequireProjectScope(ShortName);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("project.not_found", problem.ProblemDetails.Extensions["code"]);
    }
}
