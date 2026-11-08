using System.Text.Json;
using System.Text.Json.Serialization;

using Core.DataAccess.Queries.Contracts;
using Core.Domain.Request;
using Core.Features.Request.GetPublicRequestInfo;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

namespace Core.UnitTests.Requests;

public sealed class GetPublicRequestInfoEndpointTests
{
    private static PublicRequestSummary Summary(
        int requestState = 1,
        int requestType = 2,
        DateTimeOffset? validTo = null) => new(
        Guid.NewGuid(), "alice", "alice@example.org",
        ProjectShortName.Create("PROJ-1"), "res-1", "provider-1",
        requestState, validTo ?? DateTimeOffset.UtcNow.AddDays(1), requestType, "accept-link", "reject-link");

    private static async Task<IResult> InvokeAsync(PublicRequestSummary? summary)
    {
        var query = new Mock<IRequestQuery>();
        // The token resolves to a request exactly when a summary exists; an unknown / handled link (null
        // summary) reports null here → the endpoint's not-found. Addressed by token, never a surrogate id.
        query.Setup(q => q.FindActionLinkTargetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary is null ? null : new ActionLinkTarget(Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddDays(1)));
        query.Setup(q => q.GetPublicRequestSummaryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        return await GetPublicRequestInfoEndpoint.HandleAsync(
            new GetPublicRequestInfoEndpoint.Request("accept-link-token"), query.Object,
            new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_ReturnValidationProblem_WithNotFoundCode_When_RequestNotFound()
    {
        var result = await InvokeAsync(summary: null);

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.NotFound, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithNotPendingCode_When_RequestNotPending()
    {
        // State 4 (Accepted) is past the pending range (> PendingForHPCProject = 3).
        var result = await InvokeAsync(Summary(requestState: 4));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.NotPending, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithExpiredCode_When_ActionLinkExpired()
    {
        var result = await InvokeAsync(Summary(validTo: DateTimeOffset.UtcNow.AddDays(-1)));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.Expired, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithInvalidTypeCode_When_RequestTypeNotResourceAssignment()
    {
        // Type 1 (SelfOnboarding) is not the public action-link type (2).
        var result = await InvokeAsync(Summary(requestType: 1));

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.InvalidType, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnOk_When_PendingResourceAssignmentAndValid()
    {
        var result = await InvokeAsync(Summary());

        var ok = Assert.IsType<Ok<GetPublicRequestInfoEndpoint.Response>>(result);
        Assert.Equal("alice", ok.Value!.RequestorUserName);
        Assert.Equal("alice@example.org", ok.Value.RequestorEmail);
        Assert.Equal("PROJ-1", ok.Value.ProjectShortName);
    }

    [Fact]
    public async Task Should_EmitRequestStateAndRequestTypeByName_When_Ok()
    {
        var result = await InvokeAsync(Summary(requestState: 3, requestType: 2));

        var ok = Assert.IsType<Ok<GetPublicRequestInfoEndpoint.Response>>(result);
        var wire = JsonSerializer.SerializeToElement(ok.Value, WireJson);
        Assert.Equal("\"PendingForHPCProject\"", wire.GetProperty("RequestState").GetRawText());
        Assert.Equal("\"ProjectResourceAssignment\"", wire.GetProperty("RequestType").GetRawText());
    }
}
