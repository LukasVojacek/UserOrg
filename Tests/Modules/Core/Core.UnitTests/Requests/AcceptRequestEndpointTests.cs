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

public sealed class AcceptRequestEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly Guid TheRequestId = Guid.NewGuid();
    private const string TheSlug = "req-req-proj-1-abc123";

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static RequestAggregate BuildRequest(RequestType type, RequestState state)
    {
        var request = RequestAggregate.Create(Email.Create("req@example.com"), ProjectId.New(), UserId.New(), type);
        if (state != RequestState.Pending)
            Assert.True(request.TransitionToState(state).IsSuccess);
        request.ClearDomainEvents();
        return request;
    }

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static Caller CallerInScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static async Task<IResult> InvokeAsync(
        Caller caller,
        RequestAggregate? request,
        ProjectShortName? resolvedShortName = null)
    {
        var requestQuery = new Mock<IRequestQuery>();
        // Slug resolves to the surrogate id exactly when a request exists; a "missing" request models an
        // unknown / v1 (null-slug) row, which the query reports as null → the handler's 404.
        requestQuery.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(request is null ? (Guid?)null : TheRequestId);

        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(request));

        var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(BuildProject()));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(Domain.User.User.CreateUser(
                Email.Create("req@example.com"), UserName.Create("requester"), IdpId.Create("idp-req"))));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
        directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdpId.Create("idp-req"));

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedShortName ?? ShortName);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        var notifications = new Mock<INotificationSender>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await AcceptRequestEndpoint.HandleAsync(
            new AcceptRequestEndpoint.Request(TheSlug),
            caller, requestQuery.Object, requests.Object, projects.Object, users.Object, directory.Object,
            projectDirectory.Object, roleList.Object,
            Options.Create(new WellKnownRoles()), notifications.Object, unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_AcceptingPendingSelfOnboarding()
    {
        var result = await InvokeAsync(CallerInScope(), BuildRequest(RequestType.SelfOnboarding, RequestState.Pending));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_RequestMissing()
    {
        var result = await InvokeAsync(CallerInScope(), request: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
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
    public async Task Should_ReturnConflict_When_AlreadyAccepted()
    {
        var request = BuildRequest(RequestType.SelfOnboarding, RequestState.Accepted);

        var result = await InvokeAsync(CallerInScope(), request);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }
}
