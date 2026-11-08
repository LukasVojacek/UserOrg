using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;
using Core.Domain.Shared;
using Core.Features.OnboardingRequest.RejectOnboardingRequest;
using Core.Features.Request.Notifications;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;

namespace Core.UnitTests.Requests;

public sealed class RejectOnboardingRequestEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly ProjectShortName OtherShortName = ProjectShortName.Create("PROJ-2");
    private static readonly IdpId RequesterIdpId = IdpId.Create("idp-req");
    private const string TheSlug = "req-req-proj-1-abc123";

    private static OnboardingRequestAggregate BuildRequest(OnboardingRequestState? settledTo = null)
    {
        var request = OnboardingRequestAggregate.Create(
            ShortName, RequesterIdpId, Email.Create("req@example.com"),
            OnboardingRequestSlug.Create(TheSlug));
        if (settledTo is { } target)
            Assert.True((target == OnboardingRequestState.Accepted ? request.Accept() : request.Reject()).IsSuccess);
        request.ClearDomainEvents();
        return request;
    }

    private static Caller CallerInScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        OnboardingRequestAggregate? request,
        OnboardingRequestSettlement? settlement,
        string slug = TheSlug)
    {
        var query = new Mock<IOnboardingRequestQuery>();
        query.Setup(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(settlement);
        query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var onboardingRequests = new Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>>();
        onboardingRequests.Setup(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(request));

        var notifications = new Mock<INotificationSender>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return await RejectOnboardingRequestEndpoint.HandleAsync(
            new RejectOnboardingRequestEndpoint.Request(ShortName.Value, slug),
            caller, query.Object, onboardingRequests.Object, notifications.Object, unitOfWork.Object,
            new RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static OnboardingRequestSettlement Settlement(ProjectShortName? project = null, OnboardingRequestState? state = null) =>
        new(project ?? ShortName, RequesterIdpId, state ?? OnboardingRequestState.Pending);

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_RejectingPending()
    {
        var request = BuildRequest();

        var result = await InvokeAsync(CallerInScope(), request, Settlement());

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal(OnboardingRequestState.Rejected, request.State);
    }

    [Fact]
    public async Task Should_ReturnConflict_When_AlreadyRejected()
    {
        var result = await InvokeAsync(
            CallerInScope(), BuildRequest(OnboardingRequestState.Rejected), Settlement(state: OnboardingRequestState.Rejected));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(OnboardingRequestErrorCodes.InvalidState, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_MissingOutOfScopeOrWrongProject()
    {
        // Existence must not leak: a genuine miss, an out-of-scope caller, and a wrong-project slug all return
        // a byte-identical not_found (ADR-0018 rider + ADR-0048 wrong-project rider).
        var missing = await InvokeAsync(CallerInScope(), request: null, settlement: null);
        var outOfScope = await InvokeAsync(CallerOutOfScope(), BuildRequest(), Settlement());
        var wrongProject = await InvokeAsync(CallerInScope(), BuildRequest(), Settlement(project: OtherShortName));

        var notThere = Assert.IsType<ProblemHttpResult>(missing);
        var hidden = Assert.IsType<ProblemHttpResult>(outOfScope);
        var mismatched = Assert.IsType<ProblemHttpResult>(wrongProject);

        Assert.Equal(StatusCodes.Status404NotFound, notThere.StatusCode);
        Assert.Equal(OnboardingRequestErrorCodes.NotFound, notThere.ProblemDetails.Extensions["code"]);

        foreach (var other in new[] { hidden, mismatched })
        {
            Assert.Equal(notThere.StatusCode, other.StatusCode);
            Assert.Equal(notThere.ProblemDetails.Extensions["code"], other.ProblemDetails.Extensions["code"]);
            Assert.Equal(notThere.ProblemDetails.Detail, other.ProblemDetails.Detail);
            Assert.Equal(notThere.ProblemDetails.Title, other.ProblemDetails.Title);
            Assert.Equal(notThere.ProblemDetails.Status, other.ProblemDetails.Status);
        }
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_SlugExceedsMaxLength()
    {
        // A settlement can be found by an unbounded-length query string, but OnboardingRequestSlug itself
        // rejects anything over MaxLength — that must surface as the same not_found, never a 500.
        var overLongSlug = new string('a', OnboardingRequestSlug.MaxLength + 1);

        var result = await InvokeAsync(CallerInScope(), BuildRequest(), Settlement(), overLongSlug);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(OnboardingRequestErrorCodes.NotFound, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Request not found.", problem.ProblemDetails.Detail);
    }
}
