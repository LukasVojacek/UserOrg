using Core.DataAccess.Queries.Contracts;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request.CreateRequest;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Requests;

public sealed class CreateRequestEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static Domain.User.User BuildRequester() =>
        Domain.User.User.CreateUser(Email.Create("req@example.com"), UserName.Create("requester"), IdpId.Create("idp-req"));

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode,
            email: Email.Create("owner@example.com"));
        project.ClearDomainEvents();
        return project;
    }

    private static Caller AnyCaller() => new(
        new IdpId("idp-req"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static async Task<IResult> InvokeAsync(
        string requestType = "SelfOnboarding",
        ProjectAggregate? project = null,
        Domain.User.User? requester = null,
        Guid? pendingId = null,
        bool projectFound = true)
    {
        project ??= BuildProject();
        requester ??= BuildRequester();

        var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(projectFound ? project : null));

        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.AddAsync(It.IsAny<RequestAggregate>(), It.IsAny<CancellationToken>()))
            .Returns<RequestAggregate, CancellationToken>((r, _) => ValueTask.FromResult<RequestAggregate?>(r));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(requester));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId.New());

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectId.New());

        var query = new Mock<IRequestQuery>();
        // Guard call returns the supplied pending id (null = no duplicate); the read-back returns a fresh id.
        query.SetupSequence(q => q.FindPendingSelfOnboardingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingId)
            .ReturnsAsync(Guid.NewGuid());

        var actionLinks = new Mock<IActionLinkGenerator>();
        var notifications = new Mock<INotificationSender>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await CreateRequestEndpoint.HandleAsync(
            new CreateRequestEndpoint.Request(ShortName.Value, requestType),
            AnyCaller(), projects.Object, requests.Object, users.Object, directory.Object,
            projectDirectory.Object, query.Object, actionLinks.Object, notifications.Object,
            unitOfWork.Object, cache.Object,
            new RecordingAuditLogger<CreateRequestEndpoint.AuditCategory>(), NullLogger<CreateRequestEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    private static string? DetailOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Detail;

    [Fact]
    public async Task Should_ReturnCreated_When_ValidSelfOnboarding()
    {
        var result = await InvokeAsync();

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Should_AddressBySlug_When_ValidSelfOnboarding()
    {
        var result = await InvokeAsync();

        var created = Assert.IsType<Created<CreateRequestEndpoint.Response>>(result);
        Assert.NotNull(created.Value);
        // req-{e3}-{shortname}-{r6}: requester "req@example.com" → e3 "req", project "PROJ-1" → "proj-1".
        Assert.StartsWith("req-req-proj-1-", created.Value!.Slug);
        Assert.Equal($"v2/Requests/{created.Value.Slug}", created.Location);
    }

    [Fact]
    public async Task Should_ReturnUnprocessable_WithUnsupportedTypeCode_When_NotSelfOnboarding()
    {
        var result = await InvokeAsync(requestType: "ProjectResourceAssignment");

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusOf(result));
        Assert.Equal(RequestErrorCodes.UnsupportedType, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_WithInvalidTypeCode_When_RequestTypeNameUnknown()
    {
        var result = await InvokeAsync(requestType: "Bogus");

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(RequestErrorCodes.InvalidType, CodeOf(result));
        Assert.Equal("Request type must be a valid request type name.", DetailOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_WithRequestNotFoundCode_When_ProjectMissing()
    {
        var result = await InvokeAsync(projectFound: false);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal("request.not_found", CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_WithAlreadyMemberCode_When_AlreadyActiveMember()
    {
        var requester = BuildRequester();
        var project = BuildProject();
        project.AssignUser(requester, UserId.New());
        project.ClearDomainEvents();

        var result = await InvokeAsync(project: project, requester: requester);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal(RequestErrorCodes.AlreadyActiveMember, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_WithDuplicatePendingCode_When_PendingRequestExists()
    {
        var result = await InvokeAsync(pendingId: Guid.NewGuid());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal(RequestErrorCodes.DuplicatePendingRequest, CodeOf(result));
    }
}
