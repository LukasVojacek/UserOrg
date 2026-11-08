using System.Diagnostics;

using Core.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Core.UnitTests.Shared;

public sealed class ProblemsValidationTests
{
    [Fact]
    public void Should_EmitSpecificCodeAndMessage_When_SingleFieldValidation()
    {
        var result = Problems.Validation("request.invalid_type", "Request type must be a valid request type name.");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("request.invalid_type", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Request type must be a valid request type name.", problem.ProblemDetails.Detail);
        // Single-field: no per-field structure, just the one message + the specific code.
        Assert.False(problem.ProblemDetails.Extensions.ContainsKey("fieldErrors"));
    }

    [Fact]
    public void Should_EmitValidationFailedAndFieldCodes_When_MultiFieldValidation()
    {
        var result = Problems.Validation(
            "One or more fields are invalid.",
            [("startDate", "validation.invalid_format"), ("endDate", "validation.invalid_format")]);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("validation.failed", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("One or more fields are invalid.", problem.ProblemDetails.Detail);
        Assert.NotNull(problem.ProblemDetails.Extensions["fieldErrors"]);
    }

    [Fact]
    public void Should_StampRequestId_When_SingleFieldValidationAndActivityPresent()
    {
        using var activity = StartTraceActivity();

        var result = Problems.Validation("request.invalid_type", "Request type must be a valid request type name.");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(activity.Id, problem.ProblemDetails.Extensions["requestId"]);
    }

    [Fact]
    public void Should_StampRequestId_When_MultiFieldValidationAndActivityPresent()
    {
        using var activity = StartTraceActivity();

        var result = Problems.Validation(
            "One or more fields are invalid.",
            [("startDate", "validation.invalid_format")]);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(activity.Id, problem.ProblemDetails.Extensions["requestId"]);
    }

    [Fact]
    public void Should_StampRequestId_When_UpstreamWithUnreachableLocationsAndActivityPresent()
    {
        using var activity = StartTraceActivity();

        var result = Problems.Upstream(
            "location.unreachable",
            "One or more locations are unreachable.",
            [("hpc-1", "https://hpc-1.example.org")]);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(activity.Id, problem.ProblemDetails.Extensions["requestId"]);
    }

    [Fact]
    public void Should_StampRequestId_When_OfRoutedHelperAndActivityPresent()
    {
        using var activity = StartTraceActivity();

        var result = Problems.NotFound("project.not_found", "Project not found.");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(activity.Id, problem.ProblemDetails.Extensions["requestId"]);
    }

    private static Activity StartTraceActivity()
    {
        var activity = new Activity("test-activity");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        return activity;
    }
}
