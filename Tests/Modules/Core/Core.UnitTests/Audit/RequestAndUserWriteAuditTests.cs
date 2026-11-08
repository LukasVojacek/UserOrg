using System.Collections;
using System.Text.Json;

using Core.DataAccess.Exceptions;
using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request.CreateRequest;
using Core.Features.Request.Notifications;
using Core.Features.ResourceRequest.CancelResourceRequest;
using Core.Features.ResourceRequest.CreateResourceRequest;
using Core.Features.ResourceRequest.Flow;
using Core.Features.User.UpdateUserPreferences;
using Core.Features.UserInfo.GetExtendedUserInfo;
using Core.Features.UserInfo.GetUserInfo;
using Core.Features.UserInfo.UseCases;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using UserOrg.Shared;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using DomainUser = Core.Domain.User.User;
using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Audit;

public sealed class RequestAndUserWriteAuditTests
{
    private const string ActorId = "mgr";
    private const string Project = "proj";
    private const string ResourceSlug = "proj-gpu";
    private const string PiEmail = "pi@example.org";
    private const string SupportEmail = "support@acme.example";
    private const string RequesterEmail = "req@example.com";
    private const string OwnerEmail = "owner@example.com";
    private const string PreferenceMarker = "midnight-orchid";
    private const string OutOfScopeReason = "project.caller_out_of_scope";
    private const string DeactivatedReason = "user.deactivated";
    private const string OrphanKey = "orphan";

    private static readonly ProjectShortName ShortName = ProjectShortName.Create(Project);
    private static readonly Guid WorkflowId = Guid.CreateVersion7();
    private static readonly IdpId Sub = new("user-sub-abc");
    private static readonly Email TokenEmail = Email.Create("caller@example.com");
    private static readonly UserName TokenUserName = UserName.Create("callername");

    private static Caller CallerInScope() => new(
        new IdpId(ActorId), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId(ActorId), "Manager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller FirstLoginCaller() => new(
        Sub, "User", "Display Name", new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        Email: TokenEmail, UserName: TokenUserName);

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

    private static IReadOnlyList<string> DataValues(AuditEvent auditEvent) =>
        [.. (auditEvent.Data ?? new Dictionary<string, object?>()).Values
            .SelectMany(value => value is IEnumerable items and not string
                ? items.Cast<object?>()
                : [value])
            .Select(value => value?.ToString() ?? string.Empty)];

    private static bool FlagsOrphan(AuditEvent auditEvent) =>
        auditEvent.Data is { } data && data.TryGetValue(OrphanKey, out var flag) && flag is true;

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<IOutputCacheInvalidator> EvictingCache()
    {
        var cache = new Mock<IOutputCacheInvalidator>();
        cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        return cache;
    }

    private sealed class CreateResourceRequestHarness
    {
        internal Mock<IResourceRequestProjectionStore> Store { get; } = new();

        internal async Task<IResult> InvokeAsync(
            Caller caller,
            RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory> audit,
            RecordingLogger<CreateResourceRequestEndpoint.AuditCategory>? logger = null,
            bool flowThrows = false,
            bool hasActiveRequest = false)
        {
            var project = ProjectAggregate.Create(
                ShortName, ProjectName.Create("Project"),
                ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
                UserId.New(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1), ServiceName.Default);

            var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
            projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<ProjectAggregate?>(project));

            var providers = new Mock<IProviderQuery>();
            providers.Setup(q => q.GetProviderDetailsAsync(It.IsAny<ProviderName>(), It.IsAny<IReadOnlyList<ProjectShortName>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProviderDetail(
                    ProviderName.From("acme"), null, Email.Create(SupportEmail), null, EOperationMode.Unspecified,
                    ServiceName.Default, IsPrivate: false, CountryCode: "CZ", IsDeleted: false, []));

            var query = new Mock<IProjectResourceRequestQuery>();
            query.Setup(q => q.HasActiveRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasActiveRequest);
            query.Setup(q => q.ListSlugsByBaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Store.Setup(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Store.Setup(s => s.DeleteBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var workflow = new Mock<IWorkflowClient>();
            var start = workflow.Setup(w => w.StartAsync<AssignResourceFlow>(It.IsAny<object>(), It.IsAny<SubmitOptions?>(), It.IsAny<CancellationToken>()));
            if (flowThrows)
                start.ThrowsAsync(new InvalidOperationException("engine down"));
            else
                start.ReturnsAsync(Guid.CreateVersion7());

            return await CreateResourceRequestEndpoint.HandleAsync(
                Project, new CreateResourceRequestEndpoint.Request("acme", "gpu", PiEmail), caller,
                projects.Object, providers.Object, query.Object, Store.Object, workflow.Object,
                audit, logger ?? new RecordingLogger<CreateResourceRequestEndpoint.AuditCategory>(), CancellationToken.None);
        }
    }

    private static ResourceRequestDetail ResourceRequestIn(
        ResourceRequestState state, string project = Project, Guid? workflowId = null) => new(
        ResourceSlug, state.Value, project, "acme", "gpu", "idp-1", PiEmail, workflowId ?? WorkflowId, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static Task<IResult> CancelAsync(
        Caller caller,
        ResourceRequestDetail? detail,
        RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory> audit,
        RecordingLogger<CancelResourceRequestEndpoint.AuditCategory>? logger = null,
        ResumeOutcome outcome = ResumeOutcome.Resumed)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.FindBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(detail);

        var workflow = new Mock<IWorkflowClient>();
        workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(outcome));
        workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(outcome));

        return CancelResourceRequestEndpoint.HandleAsync(
            new CancelResourceRequestEndpoint.Request(Project, ResourceSlug), caller, query.Object,
            new Mock<IResourceRequestProjectionStore>().Object, workflow.Object,
            audit, logger ?? new RecordingLogger<CancelResourceRequestEndpoint.AuditCategory>(), CancellationToken.None);
    }

    private sealed class CreateRequestHarness
    {
        internal Mock<INotificationSender> Notifications { get; } = new();

        internal Mock<IOutputCacheInvalidator> Cache { get; } = EvictingCache();

        internal async Task<IResult> InvokeAsync(
            RecordingAuditLogger<CreateRequestEndpoint.AuditCategory> audit,
            RecordingLogger<CreateRequestEndpoint.AuditCategory>? logger = null,
            Guid? pendingId = null)
        {
            var project = ProjectAggregate.Create(
                ShortName, ProjectName.Create("Project One"),
                ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable,
                email: Email.Create(OwnerEmail));
            project.ClearDomainEvents();

            var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
            projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<ProjectAggregate?>(project));

            var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
            requests.Setup(r => r.AddAsync(It.IsAny<RequestAggregate>(), It.IsAny<CancellationToken>()))
                .Returns<RequestAggregate, CancellationToken>((r, _) => ValueTask.FromResult<RequestAggregate?>(r));

            var users = new Mock<IRepository<DomainUser, IdpId>>();
            users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<DomainUser?>(
                    DomainUser.CreateUser(Email.Create(RequesterEmail), UserName.Create("requester"), IdpId.Create(ActorId))));

            var directory = new Mock<IUserDirectory>();
            directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(UserId.New());

            var projectDirectory = new Mock<IProjectDirectory>();
            projectDirectory.Setup(d => d.FindIdAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProjectId.New());

            var query = new Mock<IRequestQuery>();
            query.SetupSequence(q => q.FindPendingSelfOnboardingIdAsync(It.IsAny<ProjectId>(), It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(pendingId)
                .ReturnsAsync(Guid.NewGuid());

            return await CreateRequestEndpoint.HandleAsync(
                new CreateRequestEndpoint.Request(Project, "SelfOnboarding"), CallerOutOfScope(),
                projects.Object, requests.Object, users.Object, directory.Object, projectDirectory.Object, query.Object,
                new Mock<IActionLinkGenerator>().Object, Notifications.Object, SavingUnitOfWork().Object, Cache.Object,
                audit, logger ?? new RecordingLogger<CreateRequestEndpoint.AuditCategory>(), CancellationToken.None);
        }
    }

    private sealed class UpdatePreferencesHarness
    {
        internal Mock<IOutputCacheInvalidator> Cache { get; } = EvictingCache();

        internal async Task<IResult> InvokeAsync(
            RecordingAuditLogger<UpdateUserPreferencesEndpoint.AuditCategory> audit,
            RecordingLogger<UpdateUserPreferencesEndpoint.AuditCategory>? logger = null,
            string json = $$"""{"theme":"{{PreferenceMarker}}"}""",
            bool provisioned = true)
        {
            var users = new Mock<IRepository<DomainUser, IdpId>>();
            users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<DomainUser?>(
                    DomainUser.CreateUser(Email.Create(RequesterEmail), UserName.Create("requester"), IdpId.Create(ActorId))));

            var directory = new Mock<IUserDirectory>();
            directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(provisioned ? UserId.New() : null);

            return await UpdateUserPreferencesEndpoint.HandleAsync(
                new UpdateUserPreferencesEndpoint.Request(JsonDocument.Parse(json).RootElement), CallerOutOfScope(),
                users.Object, directory.Object, SavingUnitOfWork().Object, Cache.Object,
                audit, logger ?? new RecordingLogger<UpdateUserPreferencesEndpoint.AuditCategory>(), CancellationToken.None);
        }
    }

    private static Mock<IRepository<DomainUser, IdpId>> UsersWith(DomainUser? existing)
    {
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(existing));
        users.Setup(u => u.AddAsync(It.IsAny<DomainUser>(), It.IsAny<CancellationToken>()))
            .Returns((DomainUser user, CancellationToken _) => ValueTask.FromResult<DomainUser?>(user));
        return users;
    }

    private static Mock<IUserInfoQuery> Lookup(bool deactivated = false)
    {
        var query = new Mock<IUserInfoQuery>();
        query.Setup(q => q.IsDeactivatedAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(deactivated);
        query.Setup(q => q.GetBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserDetails(IdentityProviderUserId: Sub, Email: TokenEmail, UserName: TokenUserName));
        query.Setup(q => q.GetExtendedBySubAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserExtendedDetails(IdentityProviderId: Sub, Email: TokenEmail, UserName: TokenUserName, ProjectResources: []));
        return query;
    }

    private static Task<Result<Lukdrasil.Error.Error>> ProvisionAsync(
        Caller caller,
        Mock<IRepository<DomainUser, IdpId>> users,
        RecordingAuditLogger<CallerProvisioning.AuditCategory> audit,
        RecordingLogger<CallerProvisioning.AuditCategory>? logger = null,
        Mock<IUserInfoQuery>? lookup = null,
        Mock<IUnitOfWork>? unitOfWork = null) =>
        CallerProvisioning.ProvisionAsync(
            caller, users.Object, (lookup ?? Lookup()).Object, (unitOfWork ?? SavingUnitOfWork()).Object,
            audit, logger ?? new RecordingLogger<CallerProvisioning.AuditCategory>(), CancellationToken.None);

    private static Mock<IRoleListService> Roles()
    {
        var roles = new Mock<IRoleListService>();
        roles.Setup(s => s.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Lukdrasil.Error.Error>.Success(
                [new RoleInfo(1, "User", "", "default", [new PermissionInfo(new PermissionKey("perm:read"), "perm:read")], true, 1)]));
        return roles;
    }

    [Fact]
    public async Task Should_WriteSuccessWithTheSlugAndWithoutThePiEmail_When_CreatingAResourceRequestStartsTheFlow()
    {
        var audit = new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory>();

        var result = await new CreateResourceRequestHarness().InvokeAsync(CallerInScope(), audit);

        var slug = Assert.IsType<Accepted<CreateResourceRequestEndpoint.Response>>(result).Value!.Slug;
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("resource_request.self.created", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("resource_request", auditEvent.TargetType);
        Assert.Equal(slug, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(PiEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_CreatingAResourceRequestOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory>();

        var result = await new CreateResourceRequestHarness().InvokeAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("resource_request.self.created", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(PiEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheResourceRequestFlowFailsToStartAndTheRowIsRolledBack()
    {
        var audit = new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory>();
        var harness = new CreateResourceRequestHarness();

        var result = await harness.InvokeAsync(CallerInScope(), audit, flowThrows: true);

        Assert.Equal(StatusCodes.Status502BadGateway, StatusOf(result));
        harness.Store.Verify(s => s.DeleteBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_CreatingAResourceRequestConflictsWithAnActiveOne()
    {
        var audit = new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory>();

        var result = await new CreateResourceRequestHarness().InvokeAsync(CallerInScope(), audit, hasActiveRequest: true);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_KeepTheRowAndAnswer202AndLogOneWarning_When_TheResourceRequestCreateSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var logger = new RecordingLogger<CreateResourceRequestEndpoint.AuditCategory>();
        var harness = new CreateResourceRequestHarness();

        var result = await harness.InvokeAsync(CallerInScope(), audit, logger);

        Assert.Equal(StatusCodes.Status202Accepted, StatusOf(result));
        harness.Store.Verify(s => s.DeleteBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_Answer404AndLogOneWarning_When_TheResourceRequestCreateDeniedWriteFails()
    {
        var audit = new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Denied };
        var logger = new RecordingLogger<CreateResourceRequestEndpoint.AuditCategory>();

        var result = await new CreateResourceRequestHarness().InvokeAsync(CallerOutOfScope(), audit, logger);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal(AuditOutcome.Denied, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_WriteSuccessWithoutTheOrphanFlag_When_CancellingAResumedResourceRequest()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(CallerInScope(), ResourceRequestIn(ResourceRequestState.PendingPiApproval), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("resource_request.self.cancelled", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("resource_request", auditEvent.TargetType);
        Assert.Equal(ResourceSlug, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.False(FlagsOrphan(auditEvent));
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(PiEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteSuccessFlaggedAsOrphan_When_CancellingAResourceRequestWhoseWorkflowNeverStarted()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(
            CallerInScope(), ResourceRequestIn(ResourceRequestState.AwaitingResourceCreation, workflowId: Guid.Empty), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("resource_request.self.cancelled", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("resource_request", auditEvent.TargetType);
        Assert.Equal(ResourceSlug, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.True(FlagsOrphan(auditEvent));
    }

    [Fact]
    public async Task Should_WriteDenied_When_CancellingAResourceRequestOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(CallerOutOfScope(), ResourceRequestIn(ResourceRequestState.PendingPiApproval), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("resource_request.self.cancelled", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("resource_request", auditEvent.TargetType);
        Assert.Equal(ResourceSlug, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(PiEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheResourceRequestToCancelDoesNotExist()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(CallerInScope(), detail: null, audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheResourceRequestToCancelBelongsToAnotherProject()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(
            CallerInScope(), ResourceRequestIn(ResourceRequestState.PendingPiApproval, project: "other"), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheResourceRequestToCancelIsAlreadyTerminal()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(CallerInScope(), ResourceRequestIn(ResourceRequestState.Cancelled), audit);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheEngineIsNotYetAwaitingTheCancel()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(
            CallerInScope(), ResourceRequestIn(ResourceRequestState.PendingPiApproval), audit, outcome: ResumeOutcome.NotAwaiting);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Theory]
    [InlineData(AuditOutcome.Success, StatusCodes.Status204NoContent)]
    [InlineData(AuditOutcome.Denied, StatusCodes.Status404NotFound)]
    public async Task Should_AnswerAsUsualAndLogOneWarning_When_TheCancelAuditWriteFails(AuditOutcome outcome, int expectedStatus)
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory> { ThrowOn = outcome };
        var logger = new RecordingLogger<CancelResourceRequestEndpoint.AuditCategory>();
        var caller = outcome == AuditOutcome.Success ? CallerInScope() : CallerOutOfScope();

        var result = await CancelAsync(caller, ResourceRequestIn(ResourceRequestState.PendingPiApproval), audit, logger);

        Assert.Equal(expectedStatus, StatusOf(result));
        Assert.Equal(outcome, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_Answer204AndLogOneWarning_When_TheOrphanCancelSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var logger = new RecordingLogger<CancelResourceRequestEndpoint.AuditCategory>();

        var result = await CancelAsync(
            CallerInScope(), ResourceRequestIn(ResourceRequestState.PendingPiApproval, workflowId: Guid.Empty), audit, logger);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_WriteSuccessTargetingTheNewSlugWithoutEmails_When_CreatingASelfOnboardingRequest()
    {
        var audit = new RecordingAuditLogger<CreateRequestEndpoint.AuditCategory>();

        var result = await new CreateRequestHarness().InvokeAsync(audit);

        var slug = Assert.IsType<Created<CreateRequestEndpoint.Response>>(result).Value!.Slug;
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("request.self.created", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("request", auditEvent.TargetType);
        Assert.Equal(slug, auditEvent.TargetId);
        Assert.Equal(Project, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(RequesterEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(OwnerEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteNothing_When_ASelfOnboardingRequestIsAlreadyPending()
    {
        var audit = new RecordingAuditLogger<CreateRequestEndpoint.AuditCategory>();

        var result = await new CreateRequestHarness().InvokeAsync(audit, pendingId: Guid.NewGuid());

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_NotifyEvictAnswer201AndLogOneWarning_When_TheRequestCreateSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<CreateRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var logger = new RecordingLogger<CreateRequestEndpoint.AuditCategory>();
        var harness = new CreateRequestHarness();

        var result = await harness.InvokeAsync(audit, logger);

        Assert.Equal(StatusCodes.Status201Created, StatusOf(result));
        harness.Notifications.Verify(n => n.SendRequestCreatedToRequesterAsync(
            It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Cache.Verify(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_WriteSuccessTargetingTheCallerWithoutThePreferences_When_UpdatingPreferences()
    {
        var audit = new RecordingAuditLogger<UpdateUserPreferencesEndpoint.AuditCategory>();

        var result = await new UpdatePreferencesHarness().InvokeAsync(audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.preferences.updated", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(ActorId, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(PreferenceMarker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_WriteNothing_When_UpdatingPreferencesForAnUnprovisionedCaller()
    {
        var audit = new RecordingAuditLogger<UpdateUserPreferencesEndpoint.AuditCategory>();

        var result = await new UpdatePreferencesHarness().InvokeAsync(audit, provisioned: false);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_EvictAnswer204AndLogOneWarning_When_ThePreferencesSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<UpdateUserPreferencesEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var logger = new RecordingLogger<UpdateUserPreferencesEndpoint.AuditCategory>();
        var harness = new UpdatePreferencesHarness();

        var result = await harness.InvokeAsync(audit, logger);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        harness.Cache.Verify(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_WriteSuccessTargetingTheNewUserWithoutTheEmail_When_ProvisioningInsertsTheRow()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory>();
        var users = UsersWith(existing: null);

        var provisioned = await ProvisionAsync(FirstLoginCaller(), users, audit);

        Assert.True(provisioned.IsSuccess);
        users.Verify(u => u.AddAsync(It.IsAny<DomainUser>(), It.IsAny<CancellationToken>()), Times.Once);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.self.provisioned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(Sub.Value, auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(Sub.Value, auditEvent.TargetId);
        Assert.Null(auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(TokenEmail.Value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteNothing_When_ProvisioningFindsTheUserAndOnlySyncsTheEmail()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory>();
        var stale = DomainUser.CreateUser(Email.Create("old@example.com"), TokenUserName, Sub);

        var provisioned = await ProvisionAsync(FirstLoginCaller(), UsersWith(stale), audit);

        Assert.True(provisioned.IsSuccess);
        Assert.Equal(TokenEmail, stale.Email);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteNothing_When_ProvisioningLosesTheFirstLoginRace()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory>();
        var winner = DomainUser.CreateUser(TokenEmail, TokenUserName, Sub);
        var users = new Mock<IRepository<DomainUser, IdpId>>();
        var findCallCount = 0;
        users.Setup(u => u.FindAsync(Sub, It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(++findCallCount == 1 ? null : winner));
        users.Setup(u => u.AddAsync(It.IsAny<DomainUser>(), It.IsAny<CancellationToken>()))
            .Returns((DomainUser user, CancellationToken _) => ValueTask.FromResult<DomainUser?>(user));
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new Exception($"Cannot insert duplicate key row in object 'dbo.User' with unique index '{UserIdentityConflictClassifier.IdentityProviderIndexName}'.")));

        var provisioned = await ProvisionAsync(FirstLoginCaller(), users, audit, unitOfWork: unitOfWork);

        Assert.True(provisioned.IsSuccess);
        users.Verify(u => u.FindAsync(Sub, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_WriteDeniedWithTheDeactivatedCode_When_ProvisioningADeactivatedCaller()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory>();

        var provisioned = await ProvisionAsync(FirstLoginCaller(), UsersWith(existing: null), audit, lookup: Lookup(deactivated: true));

        Assert.False(provisioned.IsSuccess);
        Assert.Equal(DeactivatedReason, provisioned.Error.Code);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.self.provisioned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(Sub.Value, auditEvent.ActorId);
        Assert.Equal("user", auditEvent.TargetType);
        Assert.Equal(Sub.Value, auditEvent.TargetId);
        Assert.Equal(DeactivatedReason, auditEvent.Reason);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(TokenEmail.Value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteNothing_When_TheCallerCannotBeProvisioned()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory>();
        var users = UsersWith(existing: null);

        var provisioned = await ProvisionAsync(FirstLoginCaller() with { Email = null }, users, audit);

        Assert.False(provisioned.IsSuccess);
        users.Verify(u => u.AddAsync(It.IsAny<DomainUser>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_ProvisionAndLogOneWarning_When_TheProvisioningSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var logger = new RecordingLogger<CallerProvisioning.AuditCategory>();
        var unitOfWork = SavingUnitOfWork();

        var provisioned = await ProvisionAsync(FirstLoginCaller(), UsersWith(existing: null), audit, logger, unitOfWork: unitOfWork);

        Assert.True(provisioned.IsSuccess);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_StillRefuseAndLogOneWarning_When_TheProvisioningDeniedWriteFails()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory> { ThrowOn = AuditOutcome.Denied };
        var logger = new RecordingLogger<CallerProvisioning.AuditCategory>();

        var provisioned = await ProvisionAsync(
            FirstLoginCaller(), UsersWith(existing: null), audit, logger, lookup: Lookup(deactivated: true));

        Assert.False(provisioned.IsSuccess);
        Assert.Equal(DeactivatedReason, provisioned.Error.Code);
        Assert.Equal(AuditOutcome.Denied, Assert.Single(audit.Events).Outcome);
        Assert.Equal([LogLevel.Warning], logger.Levels);
    }

    [Fact]
    public async Task Should_WriteTheProvisioningRecordThroughTheEndpointsLogger_When_GetUserInfoIsAFirstLogin()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory>();

        var result = await GetUserInfoEndpoint.HandleAsync(
            FirstLoginCaller(), Lookup().Object, Roles().Object, UsersWith(existing: null).Object, SavingUnitOfWork().Object,
            audit, new RecordingLogger<CallerProvisioning.AuditCategory>(), CancellationToken.None);

        Assert.IsType<Ok<UserInfoResponse>>(result);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.self.provisioned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
    }

    [Fact]
    public async Task Should_WriteTheProvisioningRecordThroughTheEndpointsLogger_When_GetExtendedUserInfoIsAFirstLogin()
    {
        var audit = new RecordingAuditLogger<CallerProvisioning.AuditCategory>();

        var result = await GetExtendedUserInfoEndpoint.HandleAsync(
            FirstLoginCaller(), Lookup().Object, Roles().Object, UsersWith(existing: null).Object, SavingUnitOfWork().Object,
            audit, new RecordingLogger<CallerProvisioning.AuditCategory>(), CancellationToken.None);

        Assert.IsType<Ok<UserInfoExtendedResponse>>(result);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("user.self.provisioned", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
    }
}
