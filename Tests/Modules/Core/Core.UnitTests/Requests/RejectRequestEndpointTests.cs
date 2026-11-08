using Core.DataAccess.Queries.Contracts;
using Core.Domain.Request;
using Core.Features.Request.Notifications;
using Core.Features.Request.RejectRequest;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Requests;

public sealed class RejectRequestEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly Guid TheRequestId = Guid.NewGuid();
    private const string TheSlug = "req-req-proj-1-abc123";

    private static RequestAggregate BuildRequest(RequestType type, RequestState state)
    {
        var request = RequestAggregate.Create(Email.Create("req@example.com"), ProjectId.New(), UserId.New(), type);
        if (state != RequestState.Pending)
            Assert.True(request.TransitionToState(state).IsSuccess);
        request.ClearDomainEvents();
        return request;
    }

    private static Caller CallerInScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static async Task<IResult> InvokeAsync(Caller caller, RequestAggregate? request)
    {
        var requestQuery = new Mock<IRequestQuery>();
        // Slug resolves to the surrogate id exactly when a request exists; a "missing" request models an
        // unknown / v1 (null-slug) row, which the query reports as null → the handler's 404.
        requestQuery.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(request is null ? (Guid?)null : TheRequestId);

        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(request));

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ShortName);

        var notifications = new Mock<INotificationSender>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return await RejectRequestEndpoint.HandleAsync(
            new RejectRequestEndpoint.Request(TheSlug, null),
            caller, requestQuery.Object, requests.Object, projectDirectory.Object, notifications.Object, unitOfWork.Object,
            new RecordingAuditLogger<RejectRequestEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_RejectingPending()
    {
        var result = await InvokeAsync(CallerInScope(), BuildRequest(RequestType.SelfOnboarding, RequestState.Pending));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_OutOfScope()
    {
        var result = await InvokeAsync(CallerOutOfScope(), BuildRequest(RequestType.SelfOnboarding, RequestState.Pending));

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_MissingOrOutOfScope()
    {
        // Existence must not leak: scope-hidden and genuinely-missing are byte-identical (ADR-0018 rider).
        var missing = await InvokeAsync(CallerInScope(), request: null);
        var hidden = await InvokeAsync(CallerOutOfScope(), BuildRequest(RequestType.SelfOnboarding, RequestState.Pending));

        var notThere = Assert.IsType<ProblemHttpResult>(missing);
        var scopeHidden = Assert.IsType<ProblemHttpResult>(hidden);

        Assert.Equal(StatusCodes.Status404NotFound, notThere.StatusCode);
        Assert.Equal("request.not_found", notThere.ProblemDetails.Extensions["code"]);
        Assert.Equal(notThere.ProblemDetails.Extensions["code"], scopeHidden.ProblemDetails.Extensions["code"]);
        Assert.Equal(notThere.ProblemDetails.Detail, scopeHidden.ProblemDetails.Detail);
        Assert.Equal(notThere.ProblemDetails.Title, scopeHidden.ProblemDetails.Title);
        Assert.Equal(notThere.ProblemDetails.Status, scopeHidden.ProblemDetails.Status);
    }

    [Fact]
    public async Task Should_ReturnUnprocessable_WithUnsupportedTypeCode_When_NotSelfOnboarding()
    {
        var result = await InvokeAsync(CallerInScope(), BuildRequest(RequestType.ProjectResourceAssignment, RequestState.PendingForPIAccept));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusOf(result));
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(RequestErrorCodes.UnsupportedType, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnConflict_When_AlreadyRejected()
    {
        var result = await InvokeAsync(CallerInScope(), BuildRequest(RequestType.SelfOnboarding, RequestState.Rejected));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }
}
