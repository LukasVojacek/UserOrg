using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;
using Core.Features.OnboardingRequest.RejectOnboardingRequest;
using Core.Features.Request.Notifications;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;

namespace Core.UnitTests.Requests;

public sealed class RejectOnboardingRequestEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly IdpId RequesterIdpId = IdpId.Create("idp-req");
    private static readonly Email RequesterEmail = Email.Create("req@example.com");
    private static readonly Guid ReadBackId = Guid.NewGuid();
    private const string TheSlug = "req-req-proj-1-abc123";

    private sealed class Sut
    {
        public string RouteShortName { get; set; } = ShortName.Value;

        public string Slug { get; set; } = TheSlug;

        public OnboardingRequestSettlement? Settlement { get; set; } =
            new(ShortName, RequesterIdpId, OnboardingRequestState.Pending);

        public OnboardingRequestAggregate? Request { get; set; } = BuildRequest();

        public Mock<IOnboardingRequestQuery> Query { get; } = new();
        public Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>> OnboardingRequests { get; } = new();
        public Mock<INotificationSender> Notifications { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Sut()
        {
            Query.Setup(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Settlement);
            Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ReadBackId);
            OnboardingRequests.Setup(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Request));
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Task<IResult> InvokeAsync(string scopeProject = "PROJ-1") =>
            RejectOnboardingRequestEndpoint.HandleAsync(
                new RejectOnboardingRequestEndpoint.Request(RouteShortName, Slug),
                new Caller(new IdpId("mgr"), "Manager", null,
                    new Dictionary<string, IReadOnlySet<ProjectShortName>>
                    {
                        ["Manager"] = new HashSet<ProjectShortName> { ProjectShortName.Create(scopeProject) },
                    }),
                Query.Object, OnboardingRequests.Object, Notifications.Object, UnitOfWork.Object,
                new RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory>(), CancellationToken.None);

        public void VerifyNothingSaved()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Notifications.VerifyNoOtherCalls();
        }
    }

    private static OnboardingRequestAggregate BuildRequest()
    {
        var request = OnboardingRequestAggregate.Create(ShortName, RequesterIdpId, RequesterEmail, OnboardingRequestSlug.Create(TheSlug));
        request.ClearDomainEvents();
        return request;
    }

    private static void AssertNotFound(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(OnboardingRequestErrorCodes.NotFound, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Request not found.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_RejectSaveAndNotifyWithTheReadBackId_When_ThePendingRequestIsInScope()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        Assert.IsType<NoContent>(result);
        Assert.Equal(OnboardingRequestState.Rejected, sut.Request!.State);
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestRejectedToRequesterAsync(RequesterEmail, ReadBackId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotifyWithAnEmptyId_When_TheRejectedRequestCannotBeReadBack()
    {
        var sut = new Sut();
        sut.Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        await sut.InvokeAsync();

        sut.Notifications.Verify(n => n.SendRequestRejectedToRequesterAsync(RequesterEmail, Guid.Empty, ShortName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutQuerying_When_TheRouteShortNameIsInvalid()
    {
        var sut = new Sut { RouteShortName = "" };

        var result = await sut.InvokeAsync();

        AssertNotFound(result);
        sut.Query.Verify(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheSlugHasNoSettlement()
    {
        var sut = new Sut { Settlement = null };

        var result = await sut.InvokeAsync();

        AssertNotFound(result);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheRouteProjectIsOutOfScope()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync(scopeProject: "OTHER-1");

        AssertNotFound(result);
        Assert.Equal(OnboardingRequestState.Pending, sut.Request!.State);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheSlugBelongsToAnotherProject()
    {
        var sut = new Sut { Settlement = new(ProjectShortName.Create("OTHER-1"), RequesterIdpId, OnboardingRequestState.Pending) };

        var result = await sut.InvokeAsync();

        AssertNotFound(result);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutLoading_When_TheSlugIsMalformed()
    {
        var sut = new Sut { Slug = new string('a', OnboardingRequestSlug.MaxLength + 1) };

        var result = await sut.InvokeAsync();

        AssertNotFound(result);
        sut.OnboardingRequests.Verify(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheRowIsGoneAfterTheSettlementResolved()
    {
        var sut = new Sut { Request = null };

        var result = await sut.InvokeAsync();

        AssertNotFound(result);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnInvalidStateWithoutSaving_When_TheRequestIsAlreadyAccepted()
    {
        var sut = new Sut();
        Assert.True(sut.Request!.Accept().IsSuccess);

        var result = await sut.InvokeAsync();

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(OnboardingRequestErrorCodes.InvalidState, problem.ProblemDetails.Extensions["code"]);
        sut.VerifyNothingSaved();
    }
}
