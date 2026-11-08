using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;
using Core.Domain.Shared;
using Core.Features.OnboardingRequest.AcceptOnboardingRequest;
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

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Requests;

public sealed class AcceptOnboardingRequestEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly IdpId RequesterIdpId = IdpId.Create("idp-req");
    private static readonly Email RequesterEmail = Email.Create("req@example.com");
    private static readonly Guid ReadBackId = Guid.NewGuid();
    private const string TheSlug = "req-req-proj-1-abc123";

    private sealed class Sut
    {
        public string RouteShortName { get; set; } = ShortName.Value;

        public OnboardingRequestAggregate? Request { get; set; } = BuildRequest();

        public ProjectAggregate Project { get; } = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);

        public Domain.User.User Requester { get; } = Domain.User.User.CreateUser(RequesterEmail, UserName.Create("requester"), RequesterIdpId);

        public Mock<IOnboardingRequestQuery> Query { get; } = new();
        public Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>> OnboardingRequests { get; } = new();
        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects { get; } = new();
        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();
        public Mock<IUserDirectory> Directory { get; } = new();
        public Mock<IRoleListService> Roles { get; } = new();
        public Mock<INotificationSender> Notifications { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();

        public Sut()
        {
            Query.Setup(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OnboardingRequestSettlement(ShortName, RequesterIdpId, OnboardingRequestState.Pending));
            Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ReadBackId);
            OnboardingRequests.Setup(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Request));
            Projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<ProjectAggregate?>(Project));
            Users.Setup(u => u.FindAsync(RequesterIdpId, It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<Domain.User.User?>(Requester));
            Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
            Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([new RoleInfo(3, "Member", "Member", "Project", [], true, 10)]));
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync() =>
            AcceptOnboardingRequestEndpoint.HandleAsync(
                new AcceptOnboardingRequestEndpoint.Request(RouteShortName, TheSlug),
                new Caller(new IdpId("mgr"), "Manager", null,
                    new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ShortName } }),
                Query.Object, OnboardingRequests.Object, Projects.Object, Users.Object, Directory.Object,
                Roles.Object, Options.Create(new WellKnownRoles()), Notifications.Object, UnitOfWork.Object,
                Cache.Object, new RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);

        public void VerifyNothingSaved()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Notifications.VerifyNoOtherCalls();
            Cache.VerifyNoOtherCalls();
        }
    }

    private static OnboardingRequestAggregate BuildRequest()
    {
        var request = OnboardingRequestAggregate.Create(ShortName, RequesterIdpId, RequesterEmail, OnboardingRequestSlug.Create(TheSlug));
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
    public async Task Should_SaveNotifyWithTheReadBackIdAndEvictBothCaches_When_Accepted()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        Assert.IsType<NoContent>(result);
        Assert.Equal(OnboardingRequestState.Accepted, sut.Request!.State);
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestAcceptedToRequesterAsync(RequesterEmail, ReadBackId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.UserInfo(RequesterIdpId.Value), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotifyWithAnEmptyId_When_TheAcceptedRequestCannotBeReadBack()
    {
        var sut = new Sut();
        sut.Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        await sut.InvokeAsync();

        sut.Notifications.Verify(n => n.SendRequestAcceptedToRequesterAsync(RequesterEmail, Guid.Empty, ShortName, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutQuerying_When_TheRouteShortNameIsInvalid()
    {
        var sut = new Sut { RouteShortName = "" };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, OnboardingRequestErrorCodes.NotFound, "Request not found.");
        sut.Query.Verify(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TheRowIsGoneAfterTheSettlementResolved()
    {
        var sut = new Sut { Request = null };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, OnboardingRequestErrorCodes.NotFound, "Request not found.");
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisioned_When_TheCallerHasNoUserId()
    {
        var sut = new Sut();
        sut.Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync((UserId?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "caller.not_provisioned", "The authenticated caller is not a known user.");
        Assert.Equal(OnboardingRequestState.Pending, sut.Request!.State);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnTheAcceptFailureWithoutSaving_When_TheSettlementsRequesterIsUnknown()
    {
        var sut = new Sut();
        sut.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(null));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "onboarding_request.not_found", "Request not found.");
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnInvalidStateWithoutSaving_When_TheRequestIsAlreadyRejected()
    {
        var sut = new Sut();
        Assert.True(sut.Request!.Reject().IsSuccess);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, OnboardingRequestErrorCodes.InvalidState, null);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnRoleServiceUnavailableWithoutSaving_When_TheRolesCannotBeListed()
    {
        var sut = new Sut();
        sut.Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable", "Failed to retrieve roles.");
        Assert.Equal(OnboardingRequestState.Pending, sut.Request!.State);
        Assert.Null(sut.Project.ActiveRoleOf(sut.Requester));
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnRoleServiceUnavailableWithoutSaving_When_TheMemberRoleIsNotConfigured()
    {
        var sut = new Sut();
        sut.Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20)]));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable", "Project member role is not configured.");
        Assert.Equal(OnboardingRequestState.Pending, sut.Request!.State);
        Assert.Null(sut.Project.ActiveRoleOf(sut.Requester));
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_AcceptAndGrantTheMemberRoleMatchedIgnoringCase_When_TheCatalogNameDiffersInCase()
    {
        var sut = new Sut();
        sut.Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(
            [
                new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
                new RoleInfo(3, "MEMBER", "Member", "Project", [], true, 10),
            ]));

        var result = await sut.InvokeAsync();

        Assert.IsType<NoContent>(result);
        Assert.Equal(OnboardingRequestState.Accepted, sut.Request!.State);
        Assert.Equal(ProjectRoleId.Create(3), sut.Project.ActiveRoleOf(sut.Requester));
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
