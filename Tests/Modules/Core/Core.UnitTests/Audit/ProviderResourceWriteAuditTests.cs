using System.Collections;

using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.AddLocation;
using Core.Features.Provider.CreateProvider;
using Core.Features.Provider.DeactivateProvider;
using Core.Features.Provider.ReactivateProvider;
using Core.Features.Provider.RemoveLocation;
using Core.Features.Provider.ResourceAssignment;
using Core.Features.Provider.UpdateLocation;
using Core.Features.Provider.UpdateProvider;
using Core.Features.Request.Notifications;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.DeleteResource;
using Core.Features.Resource.PublishResource;
using Core.Features.Resource.UpdateResource;
using Core.UnitTests.Shared;
using Heappe.Contracts;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using ELocationType = Core.Domain.Shared.ELocationType;
using ProviderAggregate = Core.Domain.Provider.Provider;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Audit;

/// <summary>
/// Every provider, location, resource-assignment and resource write writes one fail-open audit record: Success
/// after the commit (or after <c>SubmitAsync</c> for the async create), Denied with reason
/// <c>provider.write_forbidden</c> when the provider-write gate refuses the caller, and nothing for any other
/// rejection. A sink that throws never changes the response and leaves one Warning naming the action.
/// </summary>
public sealed class ProviderResourceWriteAuditTests
{
    public enum Branch
    {
        Allowed,
        Denied,
        Rejected,
        CommitFails,
    }

    private const string Provider = "acme";
    private const string Location = "gpu";
    private const string ResName = "res-1";
    private const string AssignedSlug = "asg-res-1-test01";
    private const string WriteForbidden = "provider.write_forbidden";
    private const string CommitFailure = "commit failed";

    private const string SupportEmail = "support-secret@example.com";
    private const string PrimaryInvestigatorEmail = "pi-secret@example.com";
    private const string Username = "unix-login-7";
    private const string AccountString = "acct-string-9";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid FlowId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger<TCategory> : ILogger<TCategory>
    {
        internal List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record Run(Caller Caller, IResult? Result, bool CommitFailed, IReadOnlyList<AuditEvent> Events, IReadOnlyList<LogEntry> Log);

    private sealed record Scenario(
        string Action,
        string TargetType,
        int SuccessStatus,
        int RejectedStatus,
        Func<IResult, string> SuccessTargetId,
        string? DeniedTargetId,
        IReadOnlyList<string> DataNames,
        IReadOnlyList<string> Secrets,
        Func<Branch, AuditOutcome?, Task<Run>> Execute);

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static Caller OtherProviderCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    private static Caller WriterFor(Branch branch) => branch == Branch.Denied ? OtherProviderCaller() : OwnerCaller();

    private static IOptions<WellKnownRoles> Roles() => Options.Create(new WellKnownRoles());

    private static Mock<IHeappeSynchronizationPort> HeappeSynchronizationPort()
    {
        var port = new Mock<IHeappeSynchronizationPort>();
        port.Setup(p => p.TriggerSynchronizationForProjectResource(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return port;
    }

    private static Mock<IProjectCreationService> ProjectCreationService()
    {
        var service = new Mock<IProjectCreationService>();
        return service;
    }

    private static Mock<IUnitOfWork> UnitOfWorkFor(Branch branch)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        if (branch == Branch.CommitFails)
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException(CommitFailure));
        else
            unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<IProviderRepository> ProviderRepo(ProviderAggregate? provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        repo.Setup(r => r.FindIncludingDeactivatedAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        repo.Setup(r => r.FindWithAllocatedResourcesAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        return repo;
    }

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> ResourceRepo(ResourceAggregate? resource)
    {
        var repo = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(resource));
        return repo;
    }

    private static Mock<ICountryQuery> CountryQuery()
    {
        var query = new Mock<ICountryQuery>();
        query.Setup(q => q.FindIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return query;
    }

    private static Mock<ILocationQuery> LocationQuery()
    {
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.LocationTypeExistsAsync(It.IsAny<ELocationType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return query;
    }

    private static Mock<IResourceAssignmentQuery> AssignmentQuery()
    {
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        query.Setup(q => q.HasNonSharedLocationAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return query;
    }

    private static Mock<IResourceQuery> ResourceQuery(bool nameTaken)
    {
        var query = new Mock<IResourceQuery>();
        query.Setup(q => q.IsResourceNameTakenAsync(It.IsAny<ProviderName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(nameTaken);
        return query;
    }

    private static Mock<IUserDirectory> UserDirectoryQuery()
    {
        var query = new Mock<IUserDirectory>();
        query.Setup(q => q.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserId.New());
        return query;
    }

    private static Mock<IRequestApplicationService> RequestAppService(Branch branch)
    {
        var service = new Mock<IRequestApplicationService>();
        var submit = service.Setup(s => s.SubmitAsync(It.IsAny<StepRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));
        if (branch == Branch.CommitFails)
            submit.ThrowsAsync(new InvalidOperationException(CommitFailure));
        else
            submit.ReturnsAsync(new SubmitResponse(FlowId, RequestStatus.Accepted));
        return service;
    }

    private static ProviderAggregate BareProvider()
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.ClearDomainEvents();
        return provider;
    }

    private static ProviderAggregate DeactivatedProvider()
    {
        var provider = BareProvider();
        Assert.True(provider.Deactivate().IsSuccess);
        provider.ClearDomainEvents();
        return provider;
    }

    private static ProviderAggregate ProviderWithLocation()
    {
        var provider = BareProvider();
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC, isShared: true);
        provider.ClearDomainEvents();
        return provider;
    }

    private static ProviderAggregate ProviderWithAssignment()
    {
        var provider = ProviderWithLocation();
        var resource = ResourceAggregate.Create(ResourceName.From(ResName), provider, UserId.From(Guid.NewGuid()), Start, End);
        provider.AssignResource(
            LocationName.From(Location), resource, allocationUnitTypeId: 2,
            allocationUnitCount: 100, description: "d", aggregationName: "agg", specifications: [],
            resourceHasNonSharedLocation: false, slug: AssignedSlug);
        provider.ClearDomainEvents();
        return provider;
    }

    private static ResourceAggregate ExistingResource() =>
        ResourceAggregate.Create(ResourceName.From(ResName), BareProvider(), UserId.From(Guid.NewGuid()), Start, End);

    private static async Task<Run> RunAsync<TCategory>(
        Caller caller,
        AuditOutcome? throwOn,
        Func<RecordingAuditLogger<TCategory>, CapturingLogger<TCategory>, Task<IResult>> invoke)
    {
        var audit = new RecordingAuditLogger<TCategory> { ThrowOn = throwOn };
        var logger = new CapturingLogger<TCategory>();
        try
        {
            var result = await invoke(audit, logger);
            return new Run(caller, result, CommitFailed: false, audit.Events, logger.Entries);
        }
        catch (InvalidOperationException ex) when (ex.Message == CommitFailure)
        {
            return new Run(caller, Result: null, CommitFailed: true, audit.Events, logger.Entries);
        }
    }

    private static CreateResourceOnLocationEndpoint.Request CreateResourceRequest() => new(
        ResName, "desc", PrimaryInvestigatorEmail, Start, End,
        AccountString: AccountString, UnitType: EAllocationUnitType.NodeHours, MappingOneToOne: true,
        UseAccountingStringForScheduler: true, Clusters: [new("cluster", "/scratch", "/project")],
        Usernames: new List<string>() { Username }, LicenseUrl: null, BypassHeappe: false,
        AssignedLocations:
        [
            new(Location, EAllocationUnitType.NodeHours, AggregationName: "agg", AllocationUnitCount: 100,
                AllocationDescription: "alloc", PreferredAuthenticationType: "ssh", Specifications: [new("cpu", "16")]),
        ]);

    private static Mock<IProjectResourceRequestQuery> ProjectResourceRequestQueryMock()
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.FindByResourceKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceRequestDetail(
                "test-slug",
                "awaiting",
                "test-project",
                Provider,
                ResName,
                "test-idp",
                "test-email",
                Guid.NewGuid(),
                "test-reason",
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(-1)
                ));
        return query;
    }

    private static Mock<IRepository<Core.Domain.Project.Project, ProjectShortName>> ProjectRepoMock()
    {
        var projectRepo = new Mock<IRepository<Core.Domain.Project.Project, ProjectShortName>>();
        projectRepo.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Project.Create(
                ProjectShortName.From("test-project"),
                ProjectName.From("Test Project"),
                ProjectDomainType.FromName(DomainTypeName.Create("test-domain-type")),
                UserId.From(Guid.NewGuid()),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(30),
                ServiceName.Default,
                Domain.Shared.EOperationMode.Editable
                ));
        return projectRepo;
    }

    private static readonly Dictionary<string, Scenario> Scenarios = new()
    {
        ["CreateProvider"] = new(
            "provider.self.created", "provider", StatusCodes.Status201Created, StatusCodes.Status409Conflict,
            _ => Provider, Provider, [], [SupportEmail],
            (branch, throwOn) =>
            {
                var caller = branch == Branch.Denied ? OwnerCaller() : AdminCaller();
                return RunAsync<CreateProviderEndpoint.AuditCategory>(caller, throwOn, (audit, logger) =>
                {
                    var providerQuery = new Mock<IProviderQuery>();
                    providerQuery.Setup(q => q.ProviderExistsAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(branch == Branch.Rejected);
                    return CreateProviderEndpoint.HandleAsync(
                        new CreateProviderEndpoint.Request(Provider, "CZ", "desc", SupportEmail, IsPrivate: false),
                        caller, ProviderRepo(null).Object, providerQuery.Object, CountryQuery().Object,
                        Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None);
                });
            }),

        ["UpdateProvider"] = new(
            "provider.self.updated", "provider", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => Provider, Provider, [], [SupportEmail],
            (branch, throwOn) => RunAsync<UpdateProviderEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                UpdateProviderEndpoint.HandleAsync(
                    Provider, new UpdateProviderEndpoint.Request("CZ", "desc", SupportEmail, IsPrivate: false),
                    WriterFor(branch), ProviderRepo(branch == Branch.Rejected ? null : BareProvider()).Object,
                    CountryQuery().Object, Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["DeactivateProvider"] = new(
            "provider.self.deactivated", "provider", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => Provider, Provider, [], [],
            (branch, throwOn) => RunAsync<DeactivateProviderEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                DeactivateProviderEndpoint.HandleAsync(
                    Provider, WriterFor(branch), ProviderRepo(branch == Branch.Rejected ? null : BareProvider()).Object,
                    Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["ReactivateProvider"] = new(
            "provider.self.reactivated", "provider", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => Provider, Provider, [], [],
            (branch, throwOn) => RunAsync<ReactivateProviderEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                ReactivateProviderEndpoint.HandleAsync(
                    Provider, WriterFor(branch), ProviderRepo(branch == Branch.Rejected ? null : DeactivatedProvider()).Object,
                    Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["AddLocation"] = new(
            "provider.location.added", "location", StatusCodes.Status201Created, StatusCodes.Status404NotFound,
            _ => Location, Location, [Provider], [],
            (branch, throwOn) => RunAsync<AddLocationEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                AddLocationEndpoint.HandleAsync(
                    Provider, new AddLocationEndpoint.Request(Location, "https://gpu", ELocationType.HPC, IsPrivate: false, IsShared: true, Description: null),
                    WriterFor(branch), ProviderRepo(branch == Branch.Rejected ? null : BareProvider()).Object,
                    LocationQuery().Object, Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["UpdateLocation"] = new(
            "provider.location.updated", "location", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => Location, Location, [Provider], [],
            (branch, throwOn) => RunAsync<UpdateLocationEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                UpdateLocationEndpoint.HandleAsync(
                    Provider, Location, new UpdateLocationEndpoint.Request("https://gpu-2", Description: null),
                    WriterFor(branch), ProviderRepo(branch == Branch.Rejected ? null : ProviderWithLocation()).Object,
                    Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["RemoveLocation"] = new(
            "provider.location.removed", "location", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => Location, Location, [Provider], [],
            (branch, throwOn) => RunAsync<RemoveLocationEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                RemoveLocationEndpoint.HandleAsync(
                    Provider, Location, WriterFor(branch),
                    ProviderRepo(branch == Branch.Rejected ? null : ProviderWithLocation()).Object,
                    Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["AssignResource"] = new(
            "provider.resource_assignment.created", "resource_assignment", StatusCodes.Status201Created, StatusCodes.Status404NotFound,
            result => Assert.IsType<Created<AssignResourceEndpoint.Response>>(result).Value!.Slug, null, [Provider, Location], [],
            (branch, throwOn) =>
            {
                var provider = branch == Branch.Rejected ? null : ProviderWithLocation();
                return RunAsync<AssignResourceEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                    AssignResourceEndpoint.HandleAsync(
                        Provider, Location,
                        new AssignResourceEndpoint.Request(ResName, EAllocationUnitType.NodeHours, 100, "d", "agg", Specifications: null),
                        WriterFor(branch), ProviderRepo(provider).Object,
                        ResourceRepo(ResourceAggregate.Create(ResourceName.From(ResName), provider ?? BareProvider(), UserId.From(Guid.NewGuid()), Start, End)).Object,
                        AssignmentQuery().Object, Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None));
            }),

        ["UpdateAssignment"] = new(
            "provider.resource_assignment.updated", "resource_assignment", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => AssignedSlug, AssignedSlug, [Provider, Location], [],
            (branch, throwOn) => RunAsync<UpdateAssignmentEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                UpdateAssignmentEndpoint.HandleAsync(
                    Provider, Location, AssignedSlug,
                    new UpdateAssignmentEndpoint.Request(EAllocationUnitType.NodeHours, 200, "d2", "agg2", Specifications: null),
                    WriterFor(branch), ProviderRepo(branch == Branch.Rejected ? null : ProviderWithAssignment()).Object,
                    AssignmentQuery().Object, Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["RemoveAssignment"] = new(
            "provider.resource_assignment.removed", "resource_assignment", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => AssignedSlug, AssignedSlug, [Provider, Location], [],
            (branch, throwOn) => RunAsync<RemoveAssignmentEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                RemoveAssignmentEndpoint.HandleAsync(
                    Provider, Location, AssignedSlug, WriterFor(branch),
                    ProviderRepo(branch == Branch.Rejected ? null : ProviderWithAssignment()).Object,
                    Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["CreateResourceOnLocation"] = new(
            "resource.creation.requested", "resource", StatusCodes.Status202Accepted, StatusCodes.Status409Conflict,
            _ => ResName, ResName, [], [PrimaryInvestigatorEmail, Username, AccountString],
            (branch, throwOn) => RunAsync<CreateResourceOnLocationEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                CreateResourceOnLocationEndpoint.HandleAsync(
                    Provider, CreateResourceRequest(), WriterFor(branch),
                    ProviderRepo(ProviderWithLocation()).Object, ResourceQuery(nameTaken: branch == Branch.Rejected).Object,
                    Roles(), RequestAppService(branch).Object, UserDirectoryQuery().Object, audit, logger, CancellationToken.None))),

        ["UpdateResource"] = new(
            "resource.self.updated", "resource", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => ResName, ResName, [], [PrimaryInvestigatorEmail],
            (branch, throwOn) => RunAsync<UpdateResourceEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                UpdateResourceEndpoint.HandleAsync(
                    Provider, ResName, new UpdateResourceEndpoint.Request("new-desc", PrimaryInvestigatorEmail, Start, End, LicenseUrl: null),
                    WriterFor(branch), ResourceRepo(branch == Branch.Rejected ? null : ExistingResource()).Object,
                    Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["DeleteResource"] = new(
            "resource.self.deleted", "resource", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => ResName, ResName, [], [],
            (branch, throwOn) => RunAsync<DeleteResourceEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                DeleteResourceEndpoint.HandleAsync(
                    Provider, ResName, WriterFor(branch),
                    ResourceRepo(branch == Branch.Rejected ? null : ExistingResource()).Object,
                    Roles(), UnitOfWorkFor(branch).Object, audit, logger, CancellationToken.None))),

        ["PublishResource"] = new(
            "resource.self.published", "resource", StatusCodes.Status204NoContent, StatusCodes.Status404NotFound,
            _ => ResName, ResName, [], [],
            (branch, throwOn) => RunAsync<PublishResourceEndpoint.AuditCategory>(WriterFor(branch), throwOn, (audit, logger) =>
                PublishResourceEndpoint.HandleAsync(
                    Provider,
                    ResName,
                    WriterFor(branch),
                    ResourceRepo(branch == Branch.Rejected ? null : ExistingResource()).Object,
                    ProjectResourceRequestQueryMock().Object,
                    ProjectRepoMock().Object,
                    Roles(),
                    UnitOfWorkFor(branch).Object,
                    audit,
                    logger,
                    new Mock<INotificationSender>().Object,
                    ProviderRepo(ProviderWithLocation()).Object,
                    HeappeSynchronizationPort().Object,
                    CancellationToken.None))),
    };

    public static TheoryData<string> Endpoints => new(Scenarios.Keys);

    public static TheoryData<string, Branch> EndpointsByAuditedBranch
    {
        get
        {
            var data = new TheoryData<string, Branch>();
            foreach (var endpoint in Scenarios.Keys)
            {
                data.Add(endpoint, Branch.Allowed);
                data.Add(endpoint, Branch.Denied);
            }
            return data;
        }
    }

    public static TheoryData<string, Branch> EndpointsWithDataNamesByAuditedBranch
    {
        get
        {
            var data = new TheoryData<string, Branch>();
            foreach (var (endpoint, _) in Scenarios.Where(s => s.Value.DataNames.Count > 0))
            {
                data.Add(endpoint, Branch.Allowed);
                data.Add(endpoint, Branch.Denied);
            }
            return data;
        }
    }

    public static TheoryData<string, Branch> EndpointsWithSecretsByAuditedBranch
    {
        get
        {
            var data = new TheoryData<string, Branch>();
            foreach (var (endpoint, _) in Scenarios.Where(s => s.Value.Secrets.Count > 0))
            {
                data.Add(endpoint, Branch.Allowed);
                data.Add(endpoint, Branch.Denied);
            }
            return data;
        }
    }

    private static IReadOnlyList<string> DataValues(AuditEvent auditEvent) =>
        [.. (auditEvent.Data ?? new Dictionary<string, object?>()).Values
            .SelectMany(value => value is IEnumerable items and not string
                ? items.Cast<object?>()
                : [value])
            .Select(value => value?.ToString() ?? string.Empty)];

    private static int? StatusOf(IResult? result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static AuditOutcome OutcomeOf(Branch branch) => branch == Branch.Denied ? AuditOutcome.Denied : AuditOutcome.Success;

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_WriteOneSuccess_When_TheWriteGoesThrough(string endpoint)
    {
        var scenario = Scenarios[endpoint];

        var run = await scenario.Execute(Branch.Allowed, null);

        Assert.Equal(scenario.SuccessStatus, StatusOf(run.Result));
        var auditEvent = Assert.Single(run.Events);
        Assert.Equal(scenario.Action, auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(run.Caller.IdpId.Value, auditEvent.ActorId);
        Assert.Equal(scenario.TargetType, auditEvent.TargetType);
        Assert.Equal(scenario.SuccessTargetId(run.Result!), auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Null(auditEvent.Reason);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_WriteOneDenied_When_TheProviderWriteGateRefusesTheCaller(string endpoint)
    {
        var scenario = Scenarios[endpoint];

        var run = await scenario.Execute(Branch.Denied, null);

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(run.Result));
        var auditEvent = Assert.Single(run.Events);
        Assert.Equal(scenario.Action, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(run.Caller.IdpId.Value, auditEvent.ActorId);
        Assert.Equal(scenario.TargetType, auditEvent.TargetType);
        if (scenario.DeniedTargetId is not null)
            Assert.Equal(scenario.DeniedTargetId, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.Equal(WriteForbidden, auditEvent.Reason);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_WriteNothing_When_TheWriteIsRejectedForAReasonOtherThanAuthorization(string endpoint)
    {
        var scenario = Scenarios[endpoint];

        var run = await scenario.Execute(Branch.Rejected, null);

        Assert.Equal(scenario.RejectedStatus, StatusOf(run.Result));
        Assert.Empty(run.Events);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Should_WriteNothing_When_TheCommitFails(string endpoint)
    {
        var run = await Scenarios[endpoint].Execute(Branch.CommitFails, null);

        Assert.True(run.CommitFailed);
        Assert.Empty(run.Events);
    }

    [Theory]
    [MemberData(nameof(EndpointsByAuditedBranch))]
    public async Task Should_KeepTheResponseAndLogOneWarning_When_TheAuditSinkThrows(string endpoint, Branch branch)
    {
        var scenario = Scenarios[endpoint];
        var outcome = OutcomeOf(branch);

        var run = await scenario.Execute(branch, outcome);

        var expectedStatus = branch == Branch.Denied ? StatusCodes.Status403Forbidden : scenario.SuccessStatus;
        Assert.Equal(expectedStatus, StatusOf(run.Result));
        Assert.Equal(outcome, Assert.Single(run.Events).Outcome);
        var warning = Assert.Single(run.Log, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(scenario.Action, warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EndpointsWithDataNamesByAuditedBranch))]
    public async Task Should_NameTheParentsInData_When_TheTargetLivesUnderAProviderOrLocation(string endpoint, Branch branch)
    {
        var scenario = Scenarios[endpoint];

        var run = await scenario.Execute(branch, null);

        var values = DataValues(Assert.Single(run.Events));
        foreach (var name in scenario.DataNames)
            Assert.Contains(name, values);
    }

    [Theory]
    [MemberData(nameof(EndpointsWithSecretsByAuditedBranch))]
    public async Task Should_KeepPersonalDataOutOfTheRecord_When_TheRequestCarriesIt(string endpoint, Branch branch)
    {
        var scenario = Scenarios[endpoint];

        var run = await scenario.Execute(branch, null);

        var auditEvent = Assert.Single(run.Events);
        var recorded = DataValues(auditEvent).Append(auditEvent.TargetId ?? string.Empty).Append(auditEvent.Reason ?? string.Empty);
        foreach (var secret in scenario.Secrets)
            Assert.DoesNotContain(recorded, value => value.Contains(secret, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_RecordTheResourceNameInData_When_AnAssignmentIsCreated()
    {
        var run = await Scenarios["AssignResource"].Execute(Branch.Allowed, null);

        Assert.Contains(ResName, DataValues(Assert.Single(run.Events)));
    }

    [Fact]
    public async Task Should_RecordTheWorkflowRequestIdInData_When_AResourceCreationIsSubmitted()
    {
        var run = await Scenarios["CreateResourceOnLocation"].Execute(Branch.Allowed, null);

        Assert.Contains(DataValues(Assert.Single(run.Events)), value => value.Equals(FlowId.ToString(), StringComparison.OrdinalIgnoreCase));
    }
}