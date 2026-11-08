using Core.DataAccess.Queries.Contracts;
using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.GetResourceRequestAction;
using Core.Features.ResourceRequest.PublicAction;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

namespace Core.UnitTests.ResourceRequests.PublicAction;

/// <summary>
/// Handler tests for the anonymous GET action-link preview (ADR-0064): a live token yields the request detail;
/// every non-resolvable token (unknown / expired / used) yields the identical 404 so validity never leaks.
/// </summary>
public sealed class GetResourceRequestActionEndpointTests
{
    private const string Token = "live-token-value";

    private static ResourceRequestDetail Detail() => new(
        Slug: "acme-gpu",
        State: "PendingPiApproval",
        ProjectShortName: "acme",
        ProviderName: "it4i",
        ResourceName: "gpu",
        RequesterIdpId: "idp-123",
        ContactEmail: "pi@example.org",
        WorkflowRequestId: Guid.NewGuid(),
        Reason: null,
        TokenExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    private static Mock<IProjectResourceRequestQuery> Query(ResourceRequestDetail? detail)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query
            .Setup(q => q.FindByValidTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        return query;
    }

    private static Task<IResult> Handle(ResourceRequestDetail? detail, string token = Token) =>
        GetResourceRequestActionEndpoint.HandleAsync(
            new GetResourceRequestActionEndpoint.Request(token),
            Query(detail).Object,
            new RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory>(),
            CancellationToken.None);

    [Fact]
    public async Task Should_ReturnDetail_When_TokenLive()
    {
        var detail = Detail();

        var result = await Handle(detail);

        var ok = Assert.IsType<Ok<GetResourceRequestActionEndpoint.Response>>(result);
        Assert.Equal(detail.Slug, ok.Value!.Slug);
        Assert.Equal(detail.ProjectShortName, ok.Value.ProjectShortName);
        Assert.Equal(detail.ProviderName, ok.Value.ProviderName);
        Assert.Equal(detail.ResourceName, ok.Value.ResourceName);
        Assert.Equal(detail.RequesterIdpId, ok.Value.RequesterIdpId);
        Assert.Equal(detail.State, ok.Value.State);
    }

    [Fact]
    public async Task Should_LookUpByTokenHash_When_TokenSupplied()
    {
        var query = Query(Detail());

        await GetResourceRequestActionEndpoint.HandleAsync(
            new GetResourceRequestActionEndpoint.Request(Token),
            query.Object,
            new RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory>(),
            CancellationToken.None);

        var expectedHash = ResourceRequestToken.Hash(Token);
        query.Verify(
            q => q.FindByValidTokenHashAsync(expectedHash, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Return404_When_TokenUnknown()
    {
        var result = await Handle(detail: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(ResourceRequestActionProblems.ActionNotFoundCode, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_UnknownExpiredOrUsed()
    {
        // Unknown / expired / used all surface as a null lookup (the query enforces hash + TTL). Assert the three
        // resulting problems are byte-identical (same status, code, detail) so the cause can never be told apart.
        var unknown = Assert.IsType<ProblemHttpResult>(await Handle(detail: null));
        var expired = Assert.IsType<ProblemHttpResult>(await Handle(detail: null));
        var used = Assert.IsType<ProblemHttpResult>(await Handle(detail: null, token: "some-other-token"));

        foreach (var other in new[] { expired, used })
        {
            Assert.Equal(unknown.StatusCode, other.StatusCode);
            Assert.Equal(unknown.ProblemDetails.Detail, other.ProblemDetails.Detail);
            Assert.Equal(unknown.ProblemDetails.Extensions["code"], other.ProblemDetails.Extensions["code"]);
        }
    }
}
