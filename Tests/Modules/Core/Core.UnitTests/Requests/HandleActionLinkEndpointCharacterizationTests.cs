using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request.HandleActionLink;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Requests;

public sealed class HandleActionLinkEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly Guid TheRequestId = Guid.NewGuid();
    private static readonly Email RequesterEmail = Email.Create("req@example.com");
    private const string RequesterSub = "idp-req";

    private sealed class Sut
    {
        public ActionLinkTarget? Target { get; set; } =
            new(TheRequestId, ActionLinkAction.Accept, DateTimeOffset.UtcNow.AddHours(1));

        public RequestAggregate? Request { get; set; } = BuildRequest(RequestType.SelfOnboarding);

        public Mock<IRequestQuery> Query { get; } = new();
        public Mock<IRepository<RequestAggregate, RequestId>> Requests { get; } = new();
        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects { get; } = new();
        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();
        public Mock<IUserDirectory> Directory { get; } = new();
        public Mock<IProjectDirectory> ProjectDirectory { get; } = new();
        public Mock<IRoleListService> Roles { get; } = new();
        public Mock<INotificationSender> Notifications { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();

        public Sut()
        {
            Query.Setup(q => q.FindActionLinkTargetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Target);
            Requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Request));
            Projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<ProjectAggregate?>(BuildProject()));
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<Domain.User.User?>(
                    Domain.User.User.CreateUser(RequesterEmail, UserName.Create("requester"), IdpId.Create(RequesterSub))));
            Directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdpId.Create(RequesterSub));
            ProjectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync(ShortName);
            Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([new RoleInfo(3, "Member", "Member", "Project", [], true, 10)]));
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync() =>
            HandleActionLinkEndpoint.HandleAsync(
                new HandleActionLinkEndpoint.Request("the-token"),
                Query.Object, Requests.Object, Projects.Object, Users.Object, Directory.Object,
                ProjectDirectory.Object, Roles.Object, Options.Create(new WellKnownRoles()),
                Notifications.Object, UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);

        public void VerifyNothingSaved()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Notifications.VerifyNoOtherCalls();
            Cache.Verify(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    private static RequestAggregate BuildRequest(RequestType type)
    {
        var request = RequestAggregate.Create(RequesterEmail, ProjectId.New(), UserId.New(), type);
        request.AddActionLink(ActionLinkAction.Accept, "accept-token", DateTimeOffset.UtcNow.AddHours(1));
        request.AddActionLink(ActionLinkAction.Reject, "reject-token", DateTimeOffset.UtcNow.AddHours(1));
        request.ClearDomainEvents();
        return request;
    }

    private static ProjectAggregate BuildProject()
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);
        project.ClearDomainEvents();
        return project;
    }

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    private static void AssertInvalidLink(IResult result)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(RequestErrorCodes.InvalidActionLink, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Action link is invalid, expired, or already handled.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_AcceptSaveNotifyAndEvictBothCaches_When_TheLinkIsAnAcceptLink()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        var ok = Assert.IsType<Ok<HandleActionLinkEndpoint.Response>>(result);
        Assert.Equal("Request was handled.", ok.Value!.Message);
        Assert.Equal(RequestState.Accepted, sut.Request!.State);
        Assert.All(sut.Request.ActionLinks, l => Assert.True(l.IsDeleted));
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestAcceptedToRequesterAsync(RequesterEmail, TheRequestId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.UserInfo(RequesterSub), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_RejectSaveAndNotifyWithoutEvicting_When_TheLinkIsARejectLink()
    {
        var sut = new Sut { Target = new(TheRequestId, ActionLinkAction.Reject, DateTimeOffset.UtcNow.AddHours(1)) };

        var result = await sut.InvokeAsync();

        var ok = Assert.IsType<Ok<HandleActionLinkEndpoint.Response>>(result);
        Assert.Equal("Request was handled.", ok.Value!.Message);
        Assert.Equal(RequestState.Rejected, sut.Request!.State);
        Assert.All(sut.Request.ActionLinks, l => Assert.True(l.IsDeleted));
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestRejectedToRequesterAsync(RequesterEmail, TheRequestId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
        sut.Cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnInvalidActionLink_When_TheLinkedRequestIsMissing()
    {
        var sut = new Sut { Request = null };

        var result = await sut.InvokeAsync();

        AssertInvalidLink(result);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnInvalidActionLink_When_TheLinkedRequestIsNotSelfOnboarding()
    {
        var sut = new Sut { Request = BuildRequest(RequestType.ProjectResourceAssignment) };

        var result = await sut.InvokeAsync();

        AssertInvalidLink(result);
        Assert.All(sut.Request!.ActionLinks, l => Assert.False(l.IsDeleted));
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnInvalidActionLink_When_TheRequestsProjectIsGone()
    {
        var sut = new Sut();
        sut.ProjectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectShortName?)null);

        var result = await sut.InvokeAsync();

        AssertInvalidLink(result);
        Assert.All(sut.Request!.ActionLinks, l => Assert.False(l.IsDeleted));
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnTheAcceptFailureWithoutSaving_When_TheRequesterCannotBeResolved()
    {
        var sut = new Sut();
        sut.Directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync((IdpId?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found");
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnTheRejectFailureWithoutSaving_When_TheRequestIsAlreadySettled()
    {
        var sut = new Sut { Target = new(TheRequestId, ActionLinkAction.Reject, DateTimeOffset.UtcNow.AddHours(1)) };
        Assert.True(sut.Request!.Accept().IsSuccess);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, RequestErrorCodes.InvalidStateTransition);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnInvalidActionLinkWithoutSaving_When_TheLinkCarriesAnUnknownAction()
    {
        var sut = new Sut { Target = new(TheRequestId, 3, DateTimeOffset.UtcNow.AddHours(1)) };

        var result = await sut.InvokeAsync();

        AssertInvalidLink(result);
        Assert.Equal(RequestState.Pending, sut.Request!.State);
        sut.VerifyNothingSaved();
    }
}
