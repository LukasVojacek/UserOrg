using System.Collections;

using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;
using Core.Features.Cache.ResetAllCaches;
using Core.Features.Project.AssignProjectResource;
using Core.Features.Project.AssignProjectTag;
using Core.Features.Project.CreateProject;
using Core.Features.Project.CreateProjectTag;
using Core.Features.Project.UnassignProjectResource;
using Core.Features.Project.UnassignProjectTag;
using Core.Features.Project.UpdateProject;
using Core.Features.ProjectTag.DeleteProjectTag;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using DomainUser = Core.Domain.User.User;
using ProjectAggregate = Core.Domain.Project.Project;
using ProjectTagAggregate = Core.Domain.Project.ProjectTag;
using ProviderAggregate = Core.Domain.Provider.Provider;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Audit;

public sealed class ProjectWriteAuditTests
{
    private const string Project = "PROJ-1";
    private const string MalformedProject = "not a short name!";
    private const string Provider = "acme";
    private const string ResName = "res-1";
    private const string Tag = "hpc";
    private const string ContactEmail = "contact@example.com";
    private const string TagCreatorUserName = "tag-author";
    private const string OutOfScopeReason = "project.caller_out_of_scope";
    private const string ProviderWriteForbidden = "provider.write_forbidden";
    private const string ProjectTagWriteForbidden = "project_tag.write_forbidden";
    private const string CacheResetForbidden = "cache.reset_forbidden";

    private static readonly ProjectShortName ShortName = ProjectShortName.Create(Project);

    private static Caller CallerInScope() => new(
        new IdpId("u"), "ProjectManager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["ProjectManager"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("u"), "ProjectManager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller NonAdminCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderServiceCaller() => new(
        new IdpId("svc"), "ProviderService", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static Caller ForeignProviderCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    private sealed class RecordingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
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

    private static ProjectTagAggregate CatalogueTag() =>
        ProjectTagAggregate.Create(TagName.Create(Tag), UserName.Create(TagCreatorUserName));

    private static ProjectAggregate BuildProjectWithTag()
    {
        var project = BuildProject();
        Assert.True(project.AssignExistingTag(CatalogueTag()).IsSuccess);
        project.ClearDomainEvents();
        return project;
    }

    private static ResourceAggregate NewResource(EOperationMode mode = EOperationMode.Editable) =>
        ResourceAggregate.Create(
            ResourceName.Create(ResName), ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1), UserId.From(Guid.NewGuid()),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), operationMode: mode);

    private static Mock<IUnitOfWork> UnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<IOutputCacheInvalidator> Cache()
    {
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        return cache;
    }

    private static Mock<IRepository<ProjectAggregate, ProjectShortName>> ProjectRepository(ProjectAggregate? project)
    {
        var repository = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(project));
        repository.Setup(r => r.AddAsync(It.IsAny<ProjectAggregate>(), It.IsAny<CancellationToken>()))
            .Returns((ProjectAggregate p, CancellationToken _) => ValueTask.FromResult<ProjectAggregate?>(p));
        return repository;
    }

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> ResourceRepository(ResourceAggregate resource)
    {
        var repository = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repository.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ResourceAggregate?>(resource));
        return repository;
    }

    private static Mock<IResourceAssignmentQuery> AssignmentQuery()
    {
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.GetResourceSurrogateAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return query;
    }

    private static IReadOnlyList<string> DataValues(AuditEvent auditEvent) =>
        [.. (auditEvent.Data ?? new Dictionary<string, object?>()).Values
            .SelectMany(value => value is IEnumerable items and not string
                ? items.Cast<object?>()
                : [value])
            .Select(value => value?.ToString() ?? string.Empty)];

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static Task<IResult> CreateProjectAsync(
        Caller caller,
        RecordingAuditLogger<CreateProjectEndpoint.AuditCategory> audit,
        RecordingLogger<CreateProjectEndpoint.AuditCategory>? logger = null,
        ProjectAggregate? existing = null)
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<DomainUser?>(DomainUser.CreateUser(
                Email.Create("creator@example.com"), UserName.Create("creator"), IdpId.Create("creator-idp"))));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId.New());

        var domainTypes = new Mock<IProjectDomainTypeLookup>();
        domainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectDomainType.FromName(DomainTypeName.Create("HPC")));

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(
            [
                new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
                new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
            ]));

        var request = new CreateProjectEndpoint.Request(
            ShortName: Project, Name: "Project One", Description: null, ContactEmail: ContactEmail,
            StartDate: DateTimeOffset.UtcNow, EndDate: DateTimeOffset.UtcNow.AddDays(30), DomainTypeName: "HPC");

        return CreateProjectEndpoint.HandleAsync(
            request, caller, ProjectRepository(existing).Object, users.Object, directory.Object, domainTypes.Object,
            roleList.Object, Options.Create(new WellKnownRoles()), UnitOfWork().Object, Cache().Object,
            audit, logger ?? new RecordingLogger<CreateProjectEndpoint.AuditCategory>(), new DefaultHttpContext(), CancellationToken.None);
    }

    private static Task<IResult> UpdateProjectAsync(
        Caller caller,
        RecordingAuditLogger<UpdateProjectEndpoint.AuditCategory> audit,
        RecordingLogger<UpdateProjectEndpoint.AuditCategory>? logger = null,
        ProjectAggregate? project = null)
    {
        var domainTypes = new Mock<IProjectDomainTypeLookup>();
        domainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectDomainType.FromName(DomainTypeName.Create("Engineering")));

        var request = new UpdateProjectEndpoint.Request(
            Name: "Renamed", Description: "updated", ContactEmail: ContactEmail,
            StartDate: DateTimeOffset.UtcNow, EndDate: DateTimeOffset.UtcNow.AddDays(60), DomainTypeName: "Engineering");

        return UpdateProjectEndpoint.HandleAsync(
            ShortName, request, caller, ProjectRepository(project ?? BuildProject()).Object, domainTypes.Object,
            UnitOfWork().Object, Cache().Object,
            audit, logger ?? new RecordingLogger<UpdateProjectEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static Task<IResult> CreateProjectTagAsync(
        Caller caller,
        RecordingAuditLogger<CreateProjectTagEndpoint.AuditCategory> audit,
        RecordingLogger<CreateProjectTagEndpoint.AuditCategory>? logger = null,
        ProjectAggregate? project = null)
    {
        var tags = new Mock<IRepository<ProjectTagAggregate, TagName>>();
        tags.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectTagAggregate?>(null));
        tags.Setup(r => r.AddAsync(It.IsAny<ProjectTagAggregate>(), It.IsAny<CancellationToken>()))
            .Returns((ProjectTagAggregate t, CancellationToken _) => ValueTask.FromResult<ProjectTagAggregate?>(t));

        var users = new Mock<IUserInfoQuery>();
        users.Setup(u => u.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetails(new IdpId("u"), Email.Create("u@example.com"), UserName.Create(TagCreatorUserName)));

        return CreateProjectTagEndpoint.HandleAsync(
            new CreateProjectTagEndpoint.Request(ShortName, new CreateProjectTagEndpoint.Body(Tag)),
            caller, ProjectRepository(project ?? BuildProject()).Object, tags.Object, users.Object,
            UnitOfWork().Object, Cache().Object,
            audit, logger ?? new RecordingLogger<CreateProjectTagEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static Task<IResult> AssignProjectTagAsync(
        Caller caller,
        RecordingAuditLogger<AssignProjectTagEndpoint.AuditCategory> audit,
        RecordingLogger<AssignProjectTagEndpoint.AuditCategory>? logger = null)
    {
        var tags = new Mock<IRepository<ProjectTagAggregate, TagName>>();
        tags.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectTagAggregate?>(CatalogueTag()));

        return AssignProjectTagEndpoint.HandleAsync(
            new AssignProjectTagEndpoint.Request(ShortName, Tag),
            caller, ProjectRepository(BuildProject()).Object, tags.Object, UnitOfWork().Object, Cache().Object,
            audit, logger ?? new RecordingLogger<AssignProjectTagEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static Task<IResult> UnassignProjectTagAsync(
        Caller caller,
        RecordingAuditLogger<UnassignProjectTagEndpoint.AuditCategory> audit,
        RecordingLogger<UnassignProjectTagEndpoint.AuditCategory>? logger = null) =>
        UnassignProjectTagEndpoint.HandleAsync(
            new UnassignProjectTagEndpoint.Request(ShortName, Tag),
            caller, ProjectRepository(BuildProjectWithTag()).Object, UnitOfWork().Object, Cache().Object,
            audit, logger ?? new RecordingLogger<UnassignProjectTagEndpoint.AuditCategory>(), CancellationToken.None);

    private static Task<IResult> AssignProjectResourceAsync(
        Caller caller,
        RecordingAuditLogger<AssignProjectResourceEndpoint.AuditCategory> audit,
        RecordingLogger<AssignProjectResourceEndpoint.AuditCategory>? logger = null,
        string project = Project,
        EOperationMode resourceMode = EOperationMode.Editable)
    {
        var nonShared = new Mock<IProjectResourceAssignmentQuery>();
        nonShared.Setup(q => q.ProjectHasNonSharedResourceAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return AssignProjectResourceEndpoint.HandleAsync(
            new AssignProjectResourceEndpoint.Request(project, Provider, ResName),
            caller, ProjectRepository(BuildProject()).Object, ResourceRepository(NewResource(resourceMode)).Object,
            AssignmentQuery().Object, nonShared.Object, Options.Create(new WellKnownRoles()),
            UnitOfWork().Object, Cache().Object,
            audit, logger ?? new RecordingLogger<AssignProjectResourceEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static Task<IResult> UnassignProjectResourceAsync(
        Caller caller,
        RecordingAuditLogger<UnassignProjectResourceEndpoint.AuditCategory> audit,
        RecordingLogger<UnassignProjectResourceEndpoint.AuditCategory>? logger = null,
        string project = Project)
    {
        var resource = NewResource();
        var assigned = BuildProject();
        Assert.True(assigned.AssignResource(resource).IsSuccess);
        assigned.ClearDomainEvents();

        return UnassignProjectResourceEndpoint.HandleAsync(
            new UnassignProjectResourceEndpoint.Request(project, Provider, ResName),
            caller, ProjectRepository(assigned).Object, ResourceRepository(resource).Object,
            AssignmentQuery().Object, Options.Create(new WellKnownRoles()),
            UnitOfWork().Object, Cache().Object,
            audit, logger ?? new RecordingLogger<UnassignProjectResourceEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static Task<IResult> DeleteProjectTagAsync(
        Caller caller,
        RecordingAuditLogger<DeleteProjectTagEndpoint.AuditCategory> audit,
        RecordingLogger<DeleteProjectTagEndpoint.AuditCategory>? logger = null,
        bool inUse = false)
    {
        var repository = new Mock<IRepository<ProjectTagAggregate, TagName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectTagAggregate?>(CatalogueTag()));

        var query = new Mock<IProjectTagQuery>();
        query.Setup(q => q.IsTagAssignedToAnyProjectAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inUse);

        return DeleteProjectTagEndpoint.HandleAsync(
            Tag, caller, repository.Object, query.Object, Options.Create(new WellKnownRoles()), UnitOfWork().Object,
            audit, logger ?? new RecordingLogger<DeleteProjectTagEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private static Task<IResult> ResetAllCachesAsync(
        Caller caller,
        RecordingAuditLogger<ResetAllCachesEndpoint.AuditCategory> audit,
        RecordingLogger<ResetAllCachesEndpoint.AuditCategory>? logger = null) =>
        ResetAllCachesEndpoint.HandleAsync(
            caller, Options.Create(new WellKnownRoles()), Cache().Object,
            audit, logger ?? new RecordingLogger<ResetAllCachesEndpoint.AuditCategory>(), CancellationToken.None);

    [Fact]
    public async Task Should_WriteSuccessWithoutTheContactEmail_When_CreatingAProjectCommits()
    {
        var audit = new RecordingAuditLogger<CreateProjectEndpoint.AuditCategory>();

        var result = await CreateProjectAsync(CallerInScope(), audit);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.self.created", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project", auditEvent.TargetType);
        Assert.Equal(Project, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(ContactEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheShortNameToCreateIsTaken()
    {
        var audit = new RecordingAuditLogger<CreateProjectEndpoint.AuditCategory>();

        var result = await CreateProjectAsync(CallerInScope(), audit, existing: BuildProject());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccessWithoutTheContactEmail_When_UpdatingAProjectCommits()
    {
        var audit = new RecordingAuditLogger<UpdateProjectEndpoint.AuditCategory>();

        var result = await UpdateProjectAsync(CallerInScope(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.self.updated", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project", auditEvent.TargetType);
        Assert.Equal(Project, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(ContactEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_UpdatingOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<UpdateProjectEndpoint.AuditCategory>();

        var result = await UpdateProjectAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.self.updated", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project", auditEvent.TargetType);
        Assert.Equal(Project, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteNothing_When_UpdatingAReadOnlyProjectConflicts()
    {
        var audit = new RecordingAuditLogger<UpdateProjectEndpoint.AuditCategory>();

        var result = await UpdateProjectAsync(CallerInScope(), audit, project: BuildProject(EOperationMode.ReadOnly));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccessWithoutTheTagCreatorsName_When_CreatingATagCommits()
    {
        var audit = new RecordingAuditLogger<CreateProjectTagEndpoint.AuditCategory>();

        var result = await CreateProjectTagAsync(CallerInScope(), audit);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.tag.created", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(TagCreatorUserName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_CreatingATagOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<CreateProjectTagEndpoint.AuditCategory>();

        var result = await CreateProjectTagAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.tag.created", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheProjectAlreadyHasTheTagToCreate()
    {
        var audit = new RecordingAuditLogger<CreateProjectTagEndpoint.AuditCategory>();

        var result = await CreateProjectTagAsync(CallerInScope(), audit, project: BuildProjectWithTag());

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccess_When_AssigningATagCommits()
    {
        var audit = new RecordingAuditLogger<AssignProjectTagEndpoint.AuditCategory>();

        var result = await AssignProjectTagAsync(CallerInScope(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.tag.assigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(TagCreatorUserName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_AssigningATagOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<AssignProjectTagEndpoint.AuditCategory>();

        var result = await AssignProjectTagAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.tag.assigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteSuccess_When_UnassigningATagCommits()
    {
        var audit = new RecordingAuditLogger<UnassignProjectTagEndpoint.AuditCategory>();

        var result = await UnassignProjectTagAsync(CallerInScope(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.tag.unassigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
    }

    [Fact]
    public async Task Should_WriteDenied_When_UnassigningATagOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<UnassignProjectTagEndpoint.AuditCategory>();

        var result = await UnassignProjectTagAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.tag.unassigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteSuccess_When_AssigningAResourceCommits()
    {
        var audit = new RecordingAuditLogger<AssignProjectResourceEndpoint.AuditCategory>();

        var result = await AssignProjectResourceAsync(ProviderServiceCaller(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.resource.assigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("svc", auditEvent.ActorId);
        Assert.Equal("resource", auditEvent.TargetType);
        Assert.Equal(ResName, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Contains(Provider, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDeniedWithTheRouteValuesAndNoTenant_When_AssigningForAForeignProvider()
    {
        var audit = new RecordingAuditLogger<AssignProjectResourceEndpoint.AuditCategory>();

        var result = await AssignProjectResourceAsync(ForeignProviderCaller(), audit);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.resource.assigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("resource", auditEvent.TargetType);
        Assert.Equal(ResName, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Equal(ProviderWriteForbidden, auditEvent.Reason);
        Assert.Contains(Provider, DataValues(auditEvent));
        Assert.Contains(Project, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteNothing_When_AssigningAReadOnlyResourceIsRefused()
    {
        var audit = new RecordingAuditLogger<AssignProjectResourceEndpoint.AuditCategory>();

        var result = await AssignProjectResourceAsync(ProviderServiceCaller(), audit, resourceMode: EOperationMode.ReadOnly);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccess_When_UnassigningAResourceCommits()
    {
        var audit = new RecordingAuditLogger<UnassignProjectResourceEndpoint.AuditCategory>();

        var result = await UnassignProjectResourceAsync(ProviderServiceCaller(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.resource.unassigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("svc", auditEvent.ActorId);
        Assert.Equal("resource", auditEvent.TargetType);
        Assert.Equal(ResName, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Contains(Provider, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDeniedWithTheRouteValuesAndNoTenant_When_UnassigningForAForeignProvider()
    {
        var audit = new RecordingAuditLogger<UnassignProjectResourceEndpoint.AuditCategory>();

        var result = await UnassignProjectResourceAsync(ForeignProviderCaller(), audit);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project.resource.unassigned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("resource", auditEvent.TargetType);
        Assert.Equal(ResName, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Equal(ProviderWriteForbidden, auditEvent.Reason);
        Assert.Contains(Provider, DataValues(auditEvent));
        Assert.Contains(Project, DataValues(auditEvent));
    }

    public static TheoryData<string> ResourceEndpoints() => new() { "AssignProjectResource", "UnassignProjectResource" };

    [Theory]
    [MemberData(nameof(ResourceEndpoints))]
    public async Task Should_WriteDeniedWithTheUnparsedProject_When_AForeignProviderSendsAMalformedShortName(string endpoint)
    {
        var (result, events, _) = endpoint switch
        {
            "AssignProjectResource" => await WithSinkAsync<AssignProjectResourceEndpoint.AuditCategory>(
                throwOn: null, (audit, logger) => AssignProjectResourceAsync(ForeignProviderCaller(), audit, logger, project: MalformedProject)),
            "UnassignProjectResource" => await WithSinkAsync<UnassignProjectResourceEndpoint.AuditCategory>(
                throwOn: null, (audit, logger) => UnassignProjectResourceAsync(ForeignProviderCaller(), audit, logger, project: MalformedProject)),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
        };

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        var auditEvent = Assert.Single(events);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Null(auditEvent.TenantId);
        Assert.Contains(MalformedProject, DataValues(auditEvent));
    }

    [Fact]
    public async Task Should_WriteSuccessWithoutTenant_When_DeletingATagCommits()
    {
        var audit = new RecordingAuditLogger<DeleteProjectTagEndpoint.AuditCategory>();

        var result = await DeleteProjectTagAsync(AdminCaller(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project_tag.self.deleted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("admin", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(TagCreatorUserName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDeniedWithoutTenant_When_ANonAdminDeletesATag()
    {
        var audit = new RecordingAuditLogger<DeleteProjectTagEndpoint.AuditCategory>();

        var result = await DeleteProjectTagAsync(NonAdminCaller(), audit);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("project_tag.self.deleted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Equal("project_tag", auditEvent.TargetType);
        Assert.Equal(Tag, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Equal(ProjectTagWriteForbidden, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteNothing_When_DeletingATagStillInUseConflicts()
    {
        var audit = new RecordingAuditLogger<DeleteProjectTagEndpoint.AuditCategory>();

        var result = await DeleteProjectTagAsync(AdminCaller(), audit, inUse: true);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteSuccessWithoutTenant_When_AnAdminFlushesTheOutputCache()
    {
        var audit = new RecordingAuditLogger<ResetAllCachesEndpoint.AuditCategory>();

        var result = await ResetAllCachesAsync(AdminCaller(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("cache.output.flushed", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("admin", auditEvent.ActorId);
        Assert.Null(auditEvent.TenantId);
    }

    [Fact]
    public async Task Should_WriteDeniedWithoutTenant_When_ANonAdminFlushesTheOutputCache()
    {
        var audit = new RecordingAuditLogger<ResetAllCachesEndpoint.AuditCategory>();

        var result = await ResetAllCachesAsync(NonAdminCaller(), audit);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("cache.output.flushed", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u", auditEvent.ActorId);
        Assert.Null(auditEvent.TenantId);
        Assert.Equal(CacheResetForbidden, auditEvent.Reason);
    }

    private static async Task<(IResult Result, IReadOnlyList<AuditEvent> Events, IReadOnlyList<LogLevel> Levels)> WithSinkAsync<TCategory>(
        AuditOutcome? throwOn,
        Func<RecordingAuditLogger<TCategory>, RecordingLogger<TCategory>, Task<IResult>> invoke)
    {
        var audit = new RecordingAuditLogger<TCategory> { ThrowOn = throwOn };
        var logger = new RecordingLogger<TCategory>();
        var result = await invoke(audit, logger);
        return (result, audit.Events, logger.Levels);
    }

    private static Task<(IResult Result, IReadOnlyList<AuditEvent> Events, IReadOnlyList<LogLevel> Levels)> RunWithFailingSinkAsync(
        string endpoint, AuditOutcome throwOn)
    {
        var allowed = throwOn == AuditOutcome.Success;
        return endpoint switch
        {
            "CreateProject" => WithSinkAsync<CreateProjectEndpoint.AuditCategory>(throwOn, (a, l) =>
                CreateProjectAsync(CallerInScope(), a, l)),
            "UpdateProject" => WithSinkAsync<UpdateProjectEndpoint.AuditCategory>(throwOn, (a, l) =>
                UpdateProjectAsync(allowed ? CallerInScope() : CallerOutOfScope(), a, l)),
            "CreateProjectTag" => WithSinkAsync<CreateProjectTagEndpoint.AuditCategory>(throwOn, (a, l) =>
                CreateProjectTagAsync(allowed ? CallerInScope() : CallerOutOfScope(), a, l)),
            "AssignProjectTag" => WithSinkAsync<AssignProjectTagEndpoint.AuditCategory>(throwOn, (a, l) =>
                AssignProjectTagAsync(allowed ? CallerInScope() : CallerOutOfScope(), a, l)),
            "UnassignProjectTag" => WithSinkAsync<UnassignProjectTagEndpoint.AuditCategory>(throwOn, (a, l) =>
                UnassignProjectTagAsync(allowed ? CallerInScope() : CallerOutOfScope(), a, l)),
            "AssignProjectResource" => WithSinkAsync<AssignProjectResourceEndpoint.AuditCategory>(throwOn, (a, l) =>
                AssignProjectResourceAsync(allowed ? ProviderServiceCaller() : ForeignProviderCaller(), a, l)),
            "UnassignProjectResource" => WithSinkAsync<UnassignProjectResourceEndpoint.AuditCategory>(throwOn, (a, l) =>
                UnassignProjectResourceAsync(allowed ? ProviderServiceCaller() : ForeignProviderCaller(), a, l)),
            "DeleteProjectTag" => WithSinkAsync<DeleteProjectTagEndpoint.AuditCategory>(throwOn, (a, l) =>
                DeleteProjectTagAsync(allowed ? AdminCaller() : NonAdminCaller(), a, l)),
            "ResetAllCaches" => WithSinkAsync<ResetAllCachesEndpoint.AuditCategory>(throwOn, (a, l) =>
                ResetAllCachesAsync(allowed ? AdminCaller() : NonAdminCaller(), a, l)),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
        };
    }

    public static TheoryData<string, AuditOutcome, int> FailOpenWrites() => new()
    {
        { "CreateProject", AuditOutcome.Success, StatusCodes.Status201Created },
        { "UpdateProject", AuditOutcome.Success, StatusCodes.Status204NoContent },
        { "UpdateProject", AuditOutcome.Denied, StatusCodes.Status404NotFound },
        { "CreateProjectTag", AuditOutcome.Success, StatusCodes.Status201Created },
        { "CreateProjectTag", AuditOutcome.Denied, StatusCodes.Status404NotFound },
        { "AssignProjectTag", AuditOutcome.Success, StatusCodes.Status204NoContent },
        { "AssignProjectTag", AuditOutcome.Denied, StatusCodes.Status404NotFound },
        { "UnassignProjectTag", AuditOutcome.Success, StatusCodes.Status204NoContent },
        { "UnassignProjectTag", AuditOutcome.Denied, StatusCodes.Status404NotFound },
        { "AssignProjectResource", AuditOutcome.Success, StatusCodes.Status204NoContent },
        { "AssignProjectResource", AuditOutcome.Denied, StatusCodes.Status403Forbidden },
        { "UnassignProjectResource", AuditOutcome.Success, StatusCodes.Status204NoContent },
        { "UnassignProjectResource", AuditOutcome.Denied, StatusCodes.Status403Forbidden },
        { "DeleteProjectTag", AuditOutcome.Success, StatusCodes.Status204NoContent },
        { "DeleteProjectTag", AuditOutcome.Denied, StatusCodes.Status403Forbidden },
        { "ResetAllCaches", AuditOutcome.Success, StatusCodes.Status204NoContent },
        { "ResetAllCaches", AuditOutcome.Denied, StatusCodes.Status403Forbidden },
    };

    [Theory]
    [MemberData(nameof(FailOpenWrites))]
    public async Task Should_AnswerAsUsualAndLogOneWarning_When_TheAuditSinkThrows(string endpoint, AuditOutcome outcome, int expectedStatus)
    {
        var (result, events, levels) = await RunWithFailingSinkAsync(endpoint, outcome);

        Assert.Equal(expectedStatus, StatusOf(result));
        Assert.Equal(outcome, Assert.Single(events).Outcome);
        Assert.Equal([LogLevel.Warning], levels);
    }
}
