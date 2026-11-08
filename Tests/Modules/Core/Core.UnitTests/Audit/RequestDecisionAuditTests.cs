using System.Collections;
using System.Globalization;

using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain;
using Core.Domain.OnboardingRequest;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.OnboardingRequest.AcceptOnboardingRequest;
using Core.Features.OnboardingRequest.RejectOnboardingRequest;
using Core.Features.Request.AcceptRequest;
using Core.Features.Request.Notifications;
using Core.Features.Request.RejectRequest;
using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.ResendApprovalEmail;
using Core.Shared.OutputCache;
using Core.UnitTests.ResourceRequests.Flow;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Audit;

public sealed class RequestDecisionAuditTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly IdpId RequesterIdpId = IdpId.Create("idp-req");
    private const string RequesterEmail = "req@example.com";
    private const string TheSlug = "req-req-proj-1-abc123";
    private const string ActorId = "mgr";
    private const string OutOfScopeReason = "project.caller_out_of_scope";
    private const string FreeTextReason = "the requester is not a member of our group";
    private const string ResourceProject = "proj";
    private const string ResourceSlug = "proj-gpu";
    private const string PiEmail = "pi@example.org";

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller CallerInScope(ProjectShortName? project = null) => new(
        new IdpId(ActorId), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { project ?? ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId(ActorId), "Manager", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ProjectAggregate BuildProject()
    {
        var project = ProjectAggregate.Create(
            ShortName, ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")), UserId.New(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(30), ServiceName.Default, EOperationMode.Editable);
        project.ClearDomainEvents();
        return project;
    }

    private static Domain.User.User BuildRequester() =>
        Domain.User.User.CreateUser(Email.Create(RequesterEmail), UserName.Create("requester"), RequesterIdpId);

    private static OnboardingRequestAggregate BuildOnboardingRequest()
    {
        var request = OnboardingRequestAggregate.Create(
            ShortName, RequesterIdpId, Email.Create(RequesterEmail), OnboardingRequestSlug.Create(TheSlug));
        request.ClearDomainEvents();
        return request;
    }

    private static OnboardingRequestSettlement Settlement() => new(ShortName, RequesterIdpId, OnboardingRequestState.Pending);

    private static RequestAggregate BuildSelfOnboardingRequest()
    {
        var request = RequestAggregate.Create(Email.Create(RequesterEmail), ProjectId.New(), UserId.New(), RequestType.SelfOnboarding);
        request.ClearDomainEvents();
        return request;
    }

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

    private static IReadOnlyList<string> DataValues(AuditEvent auditEvent) =>
        [.. (auditEvent.Data ?? new Dictionary<string, object?>()).Values
            .SelectMany(value => value is IEnumerable items and not string
                ? items.Cast<object?>()
                : [value])
            .Select(value => value?.ToString() ?? string.Empty)];

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static void AssertSameProblem(IResult expected, IResult actual)
    {
        var first = Assert.IsType<ProblemHttpResult>(expected);
        var second = Assert.IsType<ProblemHttpResult>(actual);
        Assert.Equal(StatusCodes.Status404NotFound, first.StatusCode);
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(first.ProblemDetails.Extensions["code"], second.ProblemDetails.Extensions["code"]);
        Assert.Equal(first.ProblemDetails.Detail, second.ProblemDetails.Detail);
        Assert.Equal(first.ProblemDetails.Title, second.ProblemDetails.Title);
    }

    private static Task<IResult> AcceptOnboardingAsync(
        Caller caller,
        RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory> audit,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<INotificationSender>? notifications = null,
        Mock<IOutputCacheInvalidator>? cache = null)
    {
        var query = new Mock<IOnboardingRequestQuery>();
        query.Setup(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Settlement());
        query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var onboardingRequests = new Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>>();
        onboardingRequests.Setup(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<OnboardingRequestAggregate?>(BuildOnboardingRequest()));

        var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(BuildProject()));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(BuildRequester()));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        return AcceptOnboardingRequestEndpoint.HandleAsync(
            new AcceptOnboardingRequestEndpoint.Request(ShortName.Value, TheSlug),
            caller, query.Object, onboardingRequests.Object, projects.Object, users.Object, directory.Object,
            roleList.Object, Options.Create(new WellKnownRoles()), (notifications ?? new Mock<INotificationSender>()).Object,
            (unitOfWork ?? UnitOfWork()).Object, (cache ?? Cache()).Object, audit, new DefaultHttpContext(), CancellationToken.None);
    }

    private static Task<IResult> RejectOnboardingAsync(
        Caller caller,
        RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory> audit,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<INotificationSender>? notifications = null)
    {
        var query = new Mock<IOnboardingRequestQuery>();
        query.Setup(q => q.FindSettlementBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Settlement());
        query.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var onboardingRequests = new Mock<IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>>();
        onboardingRequests.Setup(r => r.FindAsync(It.IsAny<OnboardingRequestSlug>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<OnboardingRequestAggregate?>(BuildOnboardingRequest()));

        return RejectOnboardingRequestEndpoint.HandleAsync(
            new RejectOnboardingRequestEndpoint.Request(ShortName.Value, TheSlug),
            caller, query.Object, onboardingRequests.Object, (notifications ?? new Mock<INotificationSender>()).Object,
            (unitOfWork ?? UnitOfWork()).Object, audit, CancellationToken.None);
    }

    private static Mock<IProjectDirectory> ProjectDirectory(ProjectShortName? resolved)
    {
        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);
        return projectDirectory;
    }

    private static Mock<IRequestQuery> RequestQuery()
    {
        var requestQuery = new Mock<IRequestQuery>();
        requestQuery.Setup(q => q.FindIdBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        return requestQuery;
    }

    private static Mock<IRepository<RequestAggregate, RequestId>> Requests()
    {
        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<RequestAggregate?>(BuildSelfOnboardingRequest()));
        return requests;
    }

    private static Task<IResult> AcceptRequestAsync(
        Caller caller,
        RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory> audit,
        ProjectShortName? resolvedShortName,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<INotificationSender>? notifications = null,
        Mock<IOutputCacheInvalidator>? cache = null)
    {
        var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(BuildProject()));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(BuildRequester()));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>())).ReturnsAsync(UserId.New());
        directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(RequesterIdpId);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        return AcceptRequestEndpoint.HandleAsync(
            new AcceptRequestEndpoint.Request(TheSlug),
            caller, RequestQuery().Object, Requests().Object, projects.Object, users.Object, directory.Object,
            ProjectDirectory(resolvedShortName).Object, roleList.Object, Options.Create(new WellKnownRoles()),
            (notifications ?? new Mock<INotificationSender>()).Object, (unitOfWork ?? UnitOfWork()).Object,
            (cache ?? Cache()).Object, audit, new DefaultHttpContext(), CancellationToken.None);
    }

    private static Task<IResult> RejectRequestAsync(
        Caller caller,
        RecordingAuditLogger<RejectRequestEndpoint.AuditCategory> audit,
        ProjectShortName? resolvedShortName,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<INotificationSender>? notifications = null) =>
        RejectRequestEndpoint.HandleAsync(
            new RejectRequestEndpoint.Request(TheSlug, FreeTextReason),
            caller, RequestQuery().Object, Requests().Object, ProjectDirectory(resolvedShortName).Object,
            (notifications ?? new Mock<INotificationSender>()).Object, (unitOfWork ?? UnitOfWork()).Object,
            audit, CancellationToken.None);

    private static ResourceRequestDetail PendingDetail() => new(
        ResourceSlug, ResourceRequestState.PendingPiApproval.Value, ResourceProject, "acme", "gpu", "idp-1", PiEmail,
        Guid.CreateVersion7(), null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static ProjectResourceRequest PendingRow() => new()
    {
        Slug = ResourceSlug,
        ProjectShortName = ResourceProject,
        ProviderName = "acme",
        ResourceName = "gpu",
        RequesterIdpId = "idp-1",
        ContactEmail = PiEmail,
        State = ResourceRequestState.PendingPiApproval,
        TokenHash = ResourceRequestToken.Hash(ResourceRequestToken.Generate()),
        TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        WorkflowRequestId = Guid.CreateVersion7(),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private sealed class ResendHarness
    {
        internal ResendHarness()
        {
            var seed = PendingRow();
            OriginalHash = seed.TokenHash;
            Store.Seed(seed);
        }

        internal string? OriginalHash { get; }

        internal FakeProjectResourceRequestStore Store { get; } = new();

        internal string? SentToken { get; private set; }

        internal Task<IResult> InvokeAsync(Caller caller, RecordingAuditLogger<ResendApprovalEmailEndpoint.AuditCategory> audit)
        {
            var query = new Mock<IProjectResourceRequestQuery>();
            query.Setup(q => q.FindBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(PendingDetail());

            var notifications = new Mock<INotificationSender>();
            notifications
                .Setup(n => n.SendPiApprovalRequestedAsync(It.IsAny<Email>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ProjectShortName>(), It.IsAny<ResourceName>(), It.IsAny<CancellationToken>()))
                .Callback((Email _, string _, string token, ProjectShortName _, ResourceName _, CancellationToken _) => SentToken = token)
                .Returns(Task.CompletedTask);

            var options = Options.Create(new ResourceRequestActionLinkOptions { BaseUrl = "https://ui", DurationInHours = 120 });

            return ResendApprovalEmailEndpoint.HandleAsync(
                new ResendApprovalEmailEndpoint.Request(ResourceProject, ResourceSlug), caller, query.Object, Store,
                notifications.Object, options, audit, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Should_WriteSuccessWithTheRequesterIdpId_When_AcceptingAnOnboardingRequestCommits()
    {
        var audit = new RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory>();

        var result = await AcceptOnboardingAsync(CallerInScope(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("onboarding_request.self.accepted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("onboarding_request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Contains(RequesterIdpId.Value, DataValues(auditEvent));
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(RequesterEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_AcceptingAnOnboardingRequestOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory>();

        var result = await AcceptOnboardingAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("onboarding_request.self.accepted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("onboarding_request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(RequesterEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_CommitNotifyAndEvictBeforeThrowing_When_TheOnboardingAcceptSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<AcceptOnboardingRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();
        var notifications = new Mock<INotificationSender>();
        var cache = Cache();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AcceptOnboardingAsync(CallerInScope(), audit, unitOfWork, notifications, cache));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendRequestAcceptedToRequesterAsync(
            It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.UserInfo(RequesterIdpId.Value), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteSuccessWithTheRequesterIdpId_When_RejectingAnOnboardingRequestCommits()
    {
        var audit = new RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory>();

        var result = await RejectOnboardingAsync(CallerInScope(), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("onboarding_request.self.rejected", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("onboarding_request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Contains(RequesterIdpId.Value, DataValues(auditEvent));
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(RequesterEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_RejectingAnOnboardingRequestOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory>();

        var result = await RejectOnboardingAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("onboarding_request.self.rejected", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("onboarding_request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_CommitAndNotifyBeforeThrowing_When_TheOnboardingRejectSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<RejectOnboardingRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();
        var notifications = new Mock<INotificationSender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RejectOnboardingAsync(CallerInScope(), audit, unitOfWork, notifications));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendRequestRejectedToRequesterAsync(
            It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteSuccess_When_AcceptingARequestCommits()
    {
        var audit = new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>();

        var result = await AcceptRequestAsync(CallerInScope(), audit, ShortName);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("request.self.accepted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(RequesterEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_AcceptingARequestOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>();

        var result = await AcceptRequestAsync(CallerOutOfScope(), audit, ShortName);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("request.self.accepted", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteNothingAndAnswerTheSameNotFound_When_TheAcceptedRequestsProjectDoesNotResolve()
    {
        var deniedAudit = new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>();
        var missAudit = new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory>();

        var denied = await AcceptRequestAsync(CallerOutOfScope(), deniedAudit, ShortName);
        var miss = await AcceptRequestAsync(CallerInScope(), missAudit, resolvedShortName: null);

        AssertSameProblem(denied, miss);
        Assert.Equal(AuditOutcome.Denied, Assert.Single(deniedAudit.Events).Outcome);
        Assert.Empty(missAudit.Events);
    }

    [Fact]
    public async Task Should_CommitNotifyAndEvictBeforeThrowing_When_TheRequestAcceptSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<AcceptRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();
        var notifications = new Mock<INotificationSender>();
        var cache = Cache();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AcceptRequestAsync(CallerInScope(), audit, ShortName, unitOfWork, notifications, cache));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendRequestAcceptedToRequesterAsync(
            It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.Project(ShortName.Value), It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.EvictAsync(CacheTags.UserInfo(RequesterIdpId.Value), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteSuccessWithoutTheFreeTextReason_When_RejectingARequestCommits()
    {
        var audit = new RecordingAuditLogger<RejectRequestEndpoint.AuditCategory>();

        var result = await RejectRequestAsync(CallerInScope(), audit, ShortName);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("request.self.rejected", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(FreeTextReason, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(RequesterEmail, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(FreeTextReason, auditEvent.Reason);
    }

    [Fact]
    public async Task Should_WriteDenied_When_RejectingARequestOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<RejectRequestEndpoint.AuditCategory>();

        var result = await RejectRequestAsync(CallerOutOfScope(), audit, ShortName);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("request.self.rejected", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("request", auditEvent.TargetType);
        Assert.Equal(TheSlug, auditEvent.TargetId);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(FreeTextReason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteNothingAndAnswerTheSameNotFound_When_TheRejectedRequestsProjectDoesNotResolve()
    {
        var deniedAudit = new RecordingAuditLogger<RejectRequestEndpoint.AuditCategory>();
        var missAudit = new RecordingAuditLogger<RejectRequestEndpoint.AuditCategory>();

        var denied = await RejectRequestAsync(CallerOutOfScope(), deniedAudit, ShortName);
        var miss = await RejectRequestAsync(CallerInScope(), missAudit, resolvedShortName: null);

        AssertSameProblem(denied, miss);
        Assert.Equal(AuditOutcome.Denied, Assert.Single(deniedAudit.Events).Outcome);
        Assert.Empty(missAudit.Events);
    }

    [Fact]
    public async Task Should_CommitAndNotifyBeforeThrowing_When_TheRequestRejectSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<RejectRequestEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();
        var notifications = new Mock<INotificationSender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RejectRequestAsync(CallerInScope(), audit, ShortName, unitOfWork, notifications));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendRequestRejectedToRequesterAsync(
            It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_WriteSuccessWithTheNewTokenHashAndExpiry_When_ReissuingTheApprovalLink()
    {
        var audit = new RecordingAuditLogger<ResendApprovalEmailEndpoint.AuditCategory>();
        var harness = new ResendHarness();

        var result = await harness.InvokeAsync(CallerInScope(ProjectShortName.Create(ResourceProject)), audit);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        var row = harness.Store.Row(ResourceSlug);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("resource_request.approval_link.reissued", auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("resource_request", auditEvent.TargetType);
        Assert.Equal(ResourceSlug, auditEvent.TargetId);
        Assert.Equal(ResourceProject, auditEvent.TenantId);
        Assert.NotNull(harness.SentToken);
        Assert.NotEqual(harness.OriginalHash, row.TokenHash);
        Assert.Contains(row.TokenHash, DataValues(auditEvent));
        Assert.DoesNotContain(harness.OriginalHash, DataValues(auditEvent));
        Assert.Contains(auditEvent.Data!.Values, value => IsInstant(value, row.TokenExpiresAt!.Value));
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(harness.SentToken!, StringComparison.Ordinal));
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(PiEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_WriteDenied_When_ReissuingTheApprovalLinkOutsideTheCallersScope()
    {
        var audit = new RecordingAuditLogger<ResendApprovalEmailEndpoint.AuditCategory>();

        var result = await new ResendHarness().InvokeAsync(CallerOutOfScope(), audit);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("resource_request.approval_link.reissued", auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(ActorId, auditEvent.ActorId);
        Assert.Equal("resource_request", auditEvent.TargetType);
        Assert.Equal(ResourceSlug, auditEvent.TargetId);
        Assert.Equal(ResourceProject, auditEvent.TenantId);
        Assert.Equal(OutOfScopeReason, auditEvent.Reason);
        Assert.DoesNotContain(DataValues(auditEvent), value => value.Contains(PiEmail, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Should_StoreTheTokenAndSendTheMailBeforeThrowing_When_TheReissueSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<ResendApprovalEmailEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var harness = new ResendHarness();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.InvokeAsync(CallerInScope(ProjectShortName.Create(ResourceProject)), audit));

        Assert.NotNull(harness.SentToken);
        Assert.Equal(ResourceRequestToken.Hash(harness.SentToken!), harness.Store.Row(ResourceSlug).TokenHash);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    private static bool IsInstant(object? value, DateTimeOffset expected) => value switch
    {
        DateTimeOffset instant => instant == expected,
        DateTime instant => new DateTimeOffset(instant.ToUniversalTime(), TimeSpan.Zero) == expected,
        string text => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) && parsed == expected,
        _ => false,
    };
}
