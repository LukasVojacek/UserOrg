using Core.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Core.UnitTests.Shared;

public sealed class RemovedQueryParameterTests
{
    private static readonly RemovedQueryParameter LocationTypeId = new("locationTypeId", "locationType");

    private static ValueTask<object?> InvokeAsync(string queryString)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(queryString);

        return LocationTypeId.InvokeAsync(
            EndpointFilterInvocationContext.Create(http),
            _ => ValueTask.FromResult<object?>("handler"));
    }

    [Theory]
    [InlineData("?locationTypeId=1")]
    [InlineData("?LOCATIONTYPEID=1")]
    [InlineData("?locationTypeId=")]
    [InlineData("?locationType=HPC&locationTypeId=abc")]
    public async Task Should_ReturnRemovedParameterProblem_When_RemovedParameterSent(string queryString)
    {
        var result = await InvokeAsync(queryString);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("validation.removed_parameter", problem.ProblemDetails.Extensions["code"]);
        Assert.Contains("'locationTypeId'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Contains("'locationType'", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?locationType=HPC")]
    public async Task Should_ReachHandler_When_RemovedParameterAbsent(string queryString)
    {
        var result = await InvokeAsync(queryString);

        Assert.Equal("handler", result);
    }
}