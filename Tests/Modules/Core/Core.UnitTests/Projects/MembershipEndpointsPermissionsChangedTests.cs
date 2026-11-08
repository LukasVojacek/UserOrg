using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.OnboardingRequest;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.OnboardingRequest.AcceptOnboardingRequest;
using Core.Features.Project.AssignProjectUser;
using Core.Features.Project.ChangeProjectUserRole;
using Core.Features.Project.CreateProject;
using Core.Features.Project.DeleteProject;
using Core.Features.Project.UnassignProjectUser;
using Core.Features.Request.AcceptRequest;
using Core.Features.Request.HandleActionLink;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using DomainUser = Core.Domain.User.User;
using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Projects;

/// <summary>
/// Every v2 membership write names the IdP ids whose project scope it changed in the permissions-changed
/// header, and the header survives the audit write that follows the commit: a best-effort one that throws
/// still answers 201, and a fail-closed one that throws becomes a 500 that still carries it.
/// </summary>
public sealed class MembershipEndpointsPermissionsChangedTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string TargetIdp = "target-idp";
    private const string CreatorIdp = "creator-idp";
    private const string RequesterIdp = "idp-req";
    private const string RequesterEmail = "req@example.com";
    private const string Slug = "req-req-proj-1-abc123";
    private const int MemberRoleId = 3;
    private const int OwnerRoleId = 4;

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(OwnerRoleId, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(MemberRoleId, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller CallerInScope(string role = "Owner") => new(
        new IdpId("u"), role, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { [role] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ProjectAggregate BuildProject(EOperationMode mode = EOperationMode.Editable)
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, mode);
        project.ClearDomainEvents();
        return project;
    }

    private static DomainUser User(string idp) =>
        DomainUser.CreateUser(Email.Create($"{idp}@example.com"), UserName.Create(idp), IdpId.Create(idp));

    private static ProjectAggregate BuildProjectWithMembers(params DomainUser[] members)
    {
        var project = BuildProject();
        foreach (var member in members)
            project.AssignUserWithRole(member, UserId.New(), ProjectRoleId.Create(MemberRoleId));
        project.ClearDomainEvents();
        return project;
    }

    private static Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects(ProjectAggregate? project)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));
        repository.Setup(r => r.AddAsync(It.IsAny<ProjectAggregate>(), It.IsAny<CancellationToken>()))
            .Returns((ProjectAggregate p, CancellationToken _) => ValueTask.FromResult<ProjectAggregate?>(p));
        return repository;
    }

    private static Mock<IRepository<DomainUser, IdpId>> Users(DomainUser user)
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DomainUser?>(user));
        return users;
    }

    private static Mock<IUserDirectory> Directory()
    {
        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
        directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdpId.Create(RequesterIdp));
        return directory;
    }

    private static Mock<IRoleListService> RoleList()
    {
        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));
        return roleList;
    }

    private static IUnitOfWork UnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork.Object;
    }

    private static IOutputCacheInvalidator Cache()
    {
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        return cache.Object;
    }

    private static RecordingAuditLogger<T> FailingAudit<T>() => new() { ThrowOn = AuditOutcome.Success };

    private static INotificationSender Notifications(bool throwing)
    {
        var notifications = new Mock<INotificationSender>();
        if (throwing)
        {
            notifications.Setup(n => n.SendRequestAcceptedToRequesterAsync(
                    It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mail relay down"));
        }

        return notifications.Object;
    }

    private static async Task<IReadOnlyList<string>> IdsOfTheFailedResponseAsync(HttpContext context, Func<Task> invoke)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(invoke);
        return PermissionsChangedResponse.Ids(await PermissionsChangedResponse.FailAsync(context));
    }

    private static Task<IResult> AssignAsync(HttpContext context, Caller caller, ProjectAggregate project, IAuditLogger<AssignProjectUserEndpoint.AuditCategory> audit) =>
        AssignProjectUserEndpoint.HandleAsync(
            new AssignProjectUserEndpoint.Request(ShortName, TargetIdp),
            caller, Projects(project).Object, Users(User(TargetIdp)).Object, Directory().Object, RoleList().Object,
            Options.Create(new WellKnownRoles()), UnitOfWork(), Cache(), audit, context, CancellationToken.None);

    private static Task<IResult> UnassignAsync(HttpContext context, IAuditLogger<UnassignProjectUserEndpoint.AuditCategory> audit)
    {
        var target = User(TargetIdp);
        return UnassignProjectUserEndpoint.HandleAsync(
            new UnassignProjectUserEndpoint.Request(ShortName, TargetIdp),
            CallerInScope(), Projects(BuildProjectWithMembers(target)).Object, Users(target).Object, Directory().Object,
            RoleList().Object, Options.Create(new WellKnownRoles()), UnitOfWork(), Cache(), audit, context, CancellationToken.None);
    }

    private static Task<IResult> ChangeRoleAsync(HttpContext context, IAuditLogger<ChangeProjectUserRoleEndpoint.AuditCategory> audit) =>
        ChangeProjectUserRoleEndpoint.HandleAsync(
            new ChangeProjectUserRoleEndpoint.Request(ShortName, TargetIdp, new ChangeProjectUserRoleEndpoint.Body("Member")),
            CallerInScope(), Projects(BuildProject()).Object, Users(User(TargetIdp)).Object,
            RoleList().Object, Options.Create(new WellKnownRoles()), UnitOfWork(), Cache(), audit, context, CancellationToken.None);

    private static Task<IResult> DeleteAsync(HttpContext context, Caller caller, IAuditLogger<DeleteProjectEndpoint.AuditCategory> audit) =>
        DeleteProjectEndpoint.HandleAsync(
            ShortName, caller, Projects(BuildProjectWithMembers(User("member-a"), User("member-b"))).Object,
            UnitOfWork(), Cache(), audit, context, CancellationToken.None);

    private static Task<IResult> CreateAsync(HttpContext context, IAuditLogger<CreateProjectEndpoint.AuditCategory> audit)
    {
        var domainTypes = new Mock<IProjectDomainTypeLookup>();
        domainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectDomainType.FromName(DomainTypeName.Create("HPC")));
        var caller = new Caller(new IdpId(CreatorIdp), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());
        var request = new CreateProjectEndpoint.Request(
            ShortName: ShortName.Value, Name: "Project One", Description: null, ContactEmail: null,
            StartDate: DateTimeOffset.UtcNow, EndDate: DateTimeOffset.UtcNow.AddDays(30), DomainTypeName: "HPC");

        return CreateProjectEndpoint.HandleAsync(
            request, caller, Projects(null).Object, Users(User(CreatorIdp)).Object, Directory().Object, domainTypes.Object,
            RoleList().Object, Options.Create(new WellKnownRoles()), UnitOfWork(), Cache(), audit,
            NullLogger<CreateProjectEndpoint.AuditCategory>.Instance, context, CancellationToken.None);
    }

    private static Task<IResult> AcceptRequestAsync(HttpContext context, IAuditLogger<AcceptRequestEndpoint.AuditCategory> audit, bool notificationThrows = false)
    {
        var requestQuery = new Mock<IRequestQuery>();
        requestQuery.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        var selfOnboarding = RequestAggregate.Create(Email.Create(RequesterEmail), ProjectId.New(), UserId.New(), RequestType.SelfOnboarding);
        selfOnboarding.ClearDomainEvents();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<RequestAggregate?>(selfOnboarding));

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync(ShortName);

        return AcceptRequestEndpoint.HandleAsync(
            new AcceptRequestEndpoint.Request(Slug),
            CallerInScope("Manager"), requestQuery.Object, requests.Object, Projects(BuildProject()).Object,
            Users(User(RequesterIdp)).Object, Directory().Object, projectDirectory.Object, RoleList().Object,
            Options.Create(new WellKnownRoles()), Notifications(notificationThrows), UnitOfWork(), Cache(), audit,
            context, CancellationToken.None);
    }

    private static Task<IResult> AcceptOnboardingAsync(HttpContext context, IAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory> audit, bool notificationThrows = false)
    {
        var query = new Mock<IOnboardingRequestQuery>();
        query.Setup(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingRequestSettlement(ShortName, IdpId.Create(RequesterIdp), OnboardingRequestState.Pending));
        query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());

        var onboardingRequest = OnboardingRequestAggregate.Create(
            ShortName, IdpId.Create(RequesterIdp), Email.Create(RequesterEmail), OnboardingRequestSlug.Create(Slug));
        onboardingRequest.ClearDomainEvents();
        var onboardingRequests = new Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>>();
        onboardingRequests.Setup(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<OnboardingRequestAggregate?>(onboardingRequest));

        return AcceptOnboardingRequestEndpoint.HandleAsync(
            new AcceptOnboardingRequestEndpoint.Request(ShortName.Value, Slug),
            CallerInScope("Manager"), query.Object, onboardingRequests.Object, Projects(BuildProject()).Object,
            Users(User(RequesterIdp)).Object, Directory().Object, RoleList().Object, Options.Create(new WellKnownRoles()),
            Notifications(notificationThrows), UnitOfWork(), Cache(), audit, context, CancellationToken.None);
    }

    private static Task<IResult> HandleActionLinkAsync(HttpContext context, IAuditLogger<HandleActionLinkEndpoint.AuditCategory> audit, bool notificationThrows = false)
    {
        var requestId = Guid.NewGuid();
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.FindActionLinkTargetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActionLinkTarget(requestId, ActionLinkAction.Accept, DateTimeOffset.UtcNow.AddHours(1)));

        var selfOnboarding = RequestAggregate.Create(
            Email.Create(RequesterEmail), ProjectId.New(), UserId.New(), RequestType.SelfOnboarding, slug: Slug);
        selfOnboarding.ClearDomainEvents();
        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<RequestAggregate?>(selfOnboarding));

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>())).ReturnsAsync(ShortName);

        return HandleActionLinkEndpoint.HandleAsync(
            new HandleActionLinkEndpoint.Request("5f0c7a52-2d7e-4c35-9d0e-8a61b2f4c9d1"),
            query.Object, requests.Object, Projects(BuildProject()).Object, Users(User(RequesterIdp)).Object,
            Directory().Object, projectDirectory.Object, RoleList().Object, Options.Create(new WellKnownRoles()),
            Notifications(notificationThrows), UnitOfWork(), Cache(), audit, context, CancellationToken.None);
    }

    [Fact]
    public async Task Should_NameTheCreator_When_CreateProjectsBestEffortAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var result = await CreateAsync(context, FailingAudit<CreateProjectEndpoint.AuditCategory>());

        Assert.Equal(StatusCodes.Status201Created, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal([CreatorIdp], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_NameTheAssignedUser_When_AssignsAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => AssignAsync(context, CallerInScope(), BuildProject(), FailingAudit<AssignProjectUserEndpoint.AuditCategory>()));

        Assert.Equal([TargetIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheUnassignedUser_When_UnassignsAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => UnassignAsync(context, FailingAudit<UnassignProjectUserEndpoint.AuditCategory>()));

        Assert.Equal([TargetIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheUserWhoseRoleChanged_When_ChangeRolesAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => ChangeRoleAsync(context, FailingAudit<ChangeProjectUserRoleEndpoint.AuditCategory>()));

        Assert.Equal([TargetIdp], ids);
    }

    [Fact]
    public async Task Should_NameEveryFormerMember_When_DeletesAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => DeleteAsync(context, CallerInScope(), FailingAudit<DeleteProjectEndpoint.AuditCategory>()));

        Assert.Equal(["member-a", "member-b"], ids);
    }

    [Fact]
    public async Task Should_NameTheRequester_When_AcceptRequestsAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => AcceptRequestAsync(context, FailingAudit<AcceptRequestEndpoint.AuditCategory>()));

        Assert.Equal([RequesterIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheRequester_When_AcceptRequestsNotificationThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => AcceptRequestAsync(context, new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>(), notificationThrows: true));

        Assert.Equal([RequesterIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheRequester_When_AcceptOnboardingRequestsAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => AcceptOnboardingAsync(context, FailingAudit<AcceptOnboardingRequestEndpoint.AuditCategory>()));

        Assert.Equal([RequesterIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheRequester_When_AcceptOnboardingRequestsNotificationThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => AcceptOnboardingAsync(context, new RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory>(), notificationThrows: true));

        Assert.Equal([RequesterIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheRequester_When_HandleActionLinksAuditThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => HandleActionLinkAsync(context, FailingAudit<HandleActionLinkEndpoint.AuditCategory>()));

        Assert.Equal([RequesterIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheRequester_When_HandleActionLinksNotificationThrows()
    {
        var context = PermissionsChangedResponse.Create();

        var ids = await IdsOfTheFailedResponseAsync(context,
            () => HandleActionLinkAsync(context, new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>(), notificationThrows: true));

        Assert.Equal([RequesterIdp], ids);
    }

    [Fact]
    public async Task Should_NameTheAssignedUser_When_AssignSucceeds()
    {
        var context = PermissionsChangedResponse.Create();

        var result = await AssignAsync(context, CallerInScope(), BuildProject(), new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>());

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal([TargetIdp], PermissionsChangedResponse.Ids(await PermissionsChangedResponse.StartAsync(context)));
    }

    [Fact]
    public async Task Should_SendNothing_When_AssignIsRefusedBeforeTheCommit()
    {
        var context = PermissionsChangedResponse.Create();

        var result = await AssignAsync(context, CallerInScope(), BuildProject(EOperationMode.ReadOnly), new RecordingAuditLogger<AssignProjectUserEndpoint.AuditCategory>());

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, (await PermissionsChangedResponse.StartAsync(context)).Count);
    }

    [Fact]
    public async Task Should_SendNothing_When_DeleteIsOutsideTheCallersScope()
    {
        var context = PermissionsChangedResponse.Create();

        var result = await DeleteAsync(context, CallerOutOfScope(), new RecordingAuditLogger<DeleteProjectEndpoint.AuditCategory>());

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(0, (await PermissionsChangedResponse.StartAsync(context)).Count);
    }
}