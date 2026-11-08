using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;
using Core.Domain.Shared;
using Core.Features.OnboardingRequest.CreateOnboardingRequest;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Requests;

public sealed class CreateOnboardingRequestEndpointTests
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

    // A caller with no project roles at all — create must NOT perform a scope check, so this still succeeds.
    private static Caller AnyCaller() => new(
        new IdpId("idp-req"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static async Task<IResult> InvokeAsync(
        Caller? caller = null,
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

        var onboardingRequests = new Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>>();
        onboardingRequests.Setup(r => r.AddAsync(It.IsAny<OnboardingRequestAggregate>(), It.IsAny<CancellationToken>()))
            .Returns<OnboardingRequestAggregate, CancellationToken>((r, _) => ValueTask.FromResult<OnboardingRequestAggregate?>(r));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(requester));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId.New());

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectId.New());

        var query = new Mock<IOnboardingRequestQuery>();
        // Duplicate-pending guard: null = no duplicate.
        query.Setup(q => q.FindPendingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingId);
        // Read-back of the surrogate id after save (for notifications).
        query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var notifications = new Mock<INotificationSender>();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);

        return await CreateOnboardingRequestEndpoint.HandleAsync(
            new CreateOnboardingRequestEndpoint.Request(ShortName.Value),
            caller ?? AnyCaller(), projects.Object, onboardingRequests.Object, users.Object, directory.Object,
            projectDirectory.Object, query.Object, notifications.Object,
            unitOfWork.Object, cache.Object, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_ReturnCreated_When_ValidOnboarding()
    {
        var result = await InvokeAsync();

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Should_AddressBySlug_When_ValidOnboarding()
    {
        var result = await InvokeAsync();

        var created = Assert.IsType<Created<CreateOnboardingRequestEndpoint.Response>>(result);
        Assert.NotNull(created.Value);
        // req-{e3}-{shortname}-{r6}: requester "req@example.com" → e3 "req", project "PROJ-1" → "proj-1".
        Assert.StartsWith("req-req-proj-1-", created.Value!.Slug);
        Assert.Equal($"v2/Projects/{ShortName.Value}/OnboardingRequests/{created.Value.Slug}", created.Location);
    }

    [Fact]
    public async Task Should_ReturnCreated_When_CallerHasNoProjectScope()
    {
        // Explicit no-scope caller — create has no RequireProjectScope, so an empty-scope caller still succeeds.
        var noScopeCaller = new Caller(
            new IdpId("idp-req"), "User", null,
            new Dictionary<string, IReadOnlySet<ProjectShortName>>());

        var result = await InvokeAsync(caller: noScopeCaller);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_WithOnboardingNotFoundCode_When_ProjectMissing()
    {
        var result = await InvokeAsync(projectFound: false);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal(OnboardingRequestErrorCodes.NotFound, CodeOf(result));
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
        Assert.Equal(OnboardingRequestErrorCodes.AlreadyActiveMember, CodeOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_WithDuplicatePendingCode_When_PendingRequestExists()
    {
        var result = await InvokeAsync(pendingId: Guid.NewGuid());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal(OnboardingRequestErrorCodes.DuplicatePendingRequest, CodeOf(result));
    }
}
