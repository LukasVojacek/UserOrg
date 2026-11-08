using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;
using Core.Features.Request.Notifications;
using Core.Features.Request.RejectRequest;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Requests;

public sealed class RejectRequestEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly Guid TheRequestId = Guid.NewGuid();
    private static readonly Email RequesterEmail = Email.Create("req@example.com");

    private sealed class Sut
    {
        public RequestAggregate? Request { get; set; } = BuildRequest();

        public Mock<IRequestQuery> Query { get; } = new();
        public Mock<IRepository<RequestAggregate, RequestId>> Requests { get; } = new();
        public Mock<IProjectDirectory> ProjectDirectory { get; } = new();
        public Mock<INotificationSender> Notifications { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        public Sut()
        {
            Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TheRequestId);
            Requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Request));
            ProjectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync(ShortName);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Task<IResult> InvokeAsync(string scopeProject = "PROJ-1") =>
            RejectRequestEndpoint.HandleAsync(
                new RejectRequestEndpoint.Request("req-req-proj-1-abc123", Reason: null),
                new Caller(new IdpId("mgr"), "Manager", null,
                    new Dictionary<string, IReadOnlySet<ProjectShortName>>
                    {
                        ["Manager"] = new HashSet<ProjectShortName> { ProjectShortName.Create(scopeProject) },
                    }),
                Query.Object, Requests.Object, ProjectDirectory.Object, Notifications.Object, UnitOfWork.Object,
                new RecordingAuditLogger<RejectRequestEndpoint.AuditCategory>(), CancellationToken.None);

        public void VerifyNothingSaved()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Notifications.VerifyNoOtherCalls();
        }
    }

    private static RequestAggregate BuildRequest(RequestType? type = null)
    {
        var request = RequestAggregate.Create(RequesterEmail, ProjectId.New(), UserId.New(), type ?? RequestType.SelfOnboarding);
        request.ClearDomainEvents();
        return request;
    }

    private static void AssertProblem(IResult result, int status, string code, string? detail)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        if (detail is not null)
            Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_RejectSaveAndNotifyTheRequester_When_ThePendingRequestIsInScope()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        Assert.IsType<NoContent>(result);
        Assert.Equal(RequestState.Rejected, sut.Request!.State);
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestRejectedToRequesterAsync(RequesterEmail, TheRequestId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutLoading_When_TheSlugDoesNotResolve()
    {
        var sut = new Sut();
        sut.Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        sut.Requests.Verify(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheSlugResolvesButTheRowIsGone()
    {
        var sut = new Sut { Request = null };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheRequestsProjectIsGone()
    {
        var sut = new Sut();
        sut.ProjectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectShortName?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        Assert.Equal(RequestState.Pending, sut.Request!.State);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutRejecting_When_TheProjectIsOutOfScope()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync(scopeProject: "OTHER-1");

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        Assert.Equal(RequestState.Pending, sut.Request!.State);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnUnsupportedType_When_TheRequestIsNotSelfOnboarding()
    {
        var sut = new Sut { Request = BuildRequest(RequestType.ProjectResourceAssignment) };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity, RequestErrorCodes.UnsupportedType, "Only self-onboarding requests can be handled on this endpoint.");
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnInvalidStateTransitionWithoutSaving_When_TheRequestIsAlreadySettled()
    {
        var sut = new Sut();
        Assert.True(sut.Request!.Accept().IsSuccess);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, RequestErrorCodes.InvalidStateTransition, null);
        sut.VerifyNothingSaved();
    }
}
