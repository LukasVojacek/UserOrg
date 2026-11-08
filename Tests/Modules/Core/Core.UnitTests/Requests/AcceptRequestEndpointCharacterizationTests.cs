using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request.AcceptRequest;
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

public sealed class AcceptRequestEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly Guid TheRequestId = Guid.NewGuid();
    private static readonly Email RequesterEmail = Email.Create("req@example.com");
    private const string RequesterSub = "idp-req";

    private sealed class Sut
    {
        public RequestAggregate? Request { get; set; } = BuildRequest();

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
            Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(TheRequestId);
            Requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Request));
            Projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<ProjectAggregate?>(BuildProject()));
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<Domain.User.User?>(
                    Domain.User.User.CreateUser(RequesterEmail, UserName.Create("requester"), IdpId.Create(RequesterSub))));
            Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
            Directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdpId.Create(RequesterSub));
            ProjectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync(ShortName);
            Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([new RoleInfo(3, "Member", "Member", "Project", [], true, 10)]));
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync() =>
            AcceptRequestEndpoint.HandleAsync(
                new AcceptRequestEndpoint.Request("req-req-proj-1-abc123"),
                new Caller(new IdpId("mgr"), "Manager", null,
                    new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ShortName } }),
                Query.Object, Requests.Object, Projects.Object, Users.Object, Directory.Object,
                ProjectDirectory.Object, Roles.Object, Options.Create(new WellKnownRoles()),
                Notifications.Object, UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);

        public void VerifyNothingSaved()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Notifications.VerifyNoOtherCalls();
            Cache.VerifyNoOtherCalls();
        }
    }

    private static RequestAggregate BuildRequest()
    {
        var request = RequestAggregate.Create(RequesterEmail, ProjectId.New(), UserId.New(), RequestType.SelfOnboarding);
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

    private static void AssertProblem(IResult result, int status, string code, string? detail)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_SaveNotifyAndEvictBothCaches_When_TheRequestIsAccepted()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        Assert.IsType<NoContent>(result);
        Assert.Equal(RequestState.Accepted, sut.Request!.State);
        sut.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.Verify(n => n.SendRequestAcceptedToRequesterAsync(RequesterEmail, TheRequestId, ShortName, It.IsAny<CancellationToken>()), Times.Once);
        sut.Notifications.VerifyNoOtherCalls();
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.Verify(c => c.EvictAsync(CacheTags.UserInfo(RequesterSub), It.IsAny<CancellationToken>()), Times.Once);
        sut.Cache.VerifyNoOtherCalls();
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
    public async Task Should_ReturnNotFound_When_TheSlugDoesNotResolve()
    {
        var sut = new Sut();
        sut.Query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "request.not_found", "Request not found.");
        sut.Requests.Verify(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()), Times.Never);
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
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnUnsupportedTypeBeforeTheScopeCheck_When_NotSelfOnboarding()
    {
        var sut = new Sut
        {
            Request = RequestAggregate.Create(RequesterEmail, ProjectId.New(), UserId.New(), RequestType.ProjectResourceAssignment),
        };
        sut.ProjectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectShortName?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity, RequestErrorCodes.UnsupportedType, "Only self-onboarding requests can be handled on this endpoint.");
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisioned_When_TheCallerHasNoUserId()
    {
        var sut = new Sut();
        sut.Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync((UserId?)null);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "caller.not_provisioned", "The authenticated caller is not a known user.");
        Assert.Equal(RequestState.Pending, sut.Request!.State);
        sut.VerifyNothingSaved();
    }

    [Fact]
    public async Task Should_ReturnTheAcceptFailureWithoutSaving_When_TheRoleServiceIsDown()
    {
        var sut = new Sut();
        sut.Roles.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable", "Failed to retrieve roles.");
        sut.VerifyNothingSaved();
    }
}
