using Core.Shared;

using Lukdrasil.Error;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Core.UnitTests.Shared;

public sealed class DomainErrorResultsTests
{
    [Fact]
    public void Should_SurfaceStableCodeAndStatus_When_MappingDomainErrorToProblem()
    {
        var error = new ErrorBuilder()
            .WithCode("project.user_already_assigned")
            .WithDescription("The user is already assigned to the project.")
            .WithSeverity(ErrorSeverity.Error)
            .Build();

        var result = error.ToProblem();

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("project.user_already_assigned", problem.ProblemDetails.Extensions["code"]);
    }
}
