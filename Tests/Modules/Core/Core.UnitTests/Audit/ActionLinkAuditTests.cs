using System.Collections;

using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;
using Core.Features.Request.GetPublicRequestInfo;
using Core.Features.Request.HandleActionLink;
using Core.Features.Request.Notifications;
using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.GetResourceRequestAction;
using Core.Features.ResourceRequest.HandleResourceRequestAction;
using Core.Features.ResourceRequest.PublicAction;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests.Audit;

public sealed class ActionLinkAuditTests
{
    public enum TokenClass
    {
        Unknown,
        Expired,
        Spent,
    }

    public enum SettleCause
    {
        RequestMissing,
        RequestNotSelfOnboarding,
        ProjectMissing,
        ActionNeitherAcceptNorReject,
    }

    public enum PreviewCause
    {
        SummaryMissing,
        NotPending,
        NotSelfOnboardingType,
    }

    public enum Route
    {
        SettleActionLink,
        PreviewActionLink,
        PreviewResourceAction,
        SettleResourceAction,
    }

    private const string SettledAction = "request.action_link.settled";
    private const string ViewedAction = "request.action_link.viewed";
    private const string ResourceViewedAction = "resource_request.action.viewed";
    private const string ResourceSettledAction = "resource_request.action.settled";

    private const string ActionLinkToken = "5f0c7a52-2d7e-4c35-9d0e-8a61b2f4c9d1";
    private const string SiblingLinkToken = "0b9d3e61-7a44-4f1c-8e25-c3d6f0a9b7e2";
    private const string ResourceToken = "rr-live-token-value";
    private const string RequesterEmail = "req@example.com";
    private const string RequesterName = "requester";
    private const string RequesterIdp = "idp-req";
    private const string RequestSlug = "proj1-req-a7x";
    private const string PiEmail = "pi@example.org";
    private const string RejectReason = "Insufficient justification";
    private const string ResourceSlug = "acme-gpu";
    private const string ResourceTenant = "acme";
    private const string ResourceRequesterIdp = "idp-123";

    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private sealed class RecordingOutputCacheInvalidator : IOutputCacheInvalidator
    {
        public List<string> EvictedTags { get; } = [];

        public ValueTask EvictAsync(string tag, CancellationToken ct = default)
        {
            EvictedTags.Add(tag);
            return ValueTask.CompletedTask;
        }
    }

    private static RequestAggregate SelfOnboardingRequest(string? slug = RequestSlug, bool rejected = false, RequestType? type = null)
    {
        var request = RequestAggregate.Create(
            Email.Create(RequesterEmail), ProjectId.New(), UserId.New(), type ?? RequestType.SelfOnboarding, slug: slug);
        if (rejected)
            Assert.True(request.Reject().IsSuccess);
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

    private static ActionLinkTarget LiveTarget(int action = ActionLinkAction.Accept) =>
        new(RequestId, action, DateTimeOffset.UtcNow.AddHours(1));

    private static ActionLinkTarget ExpiredTarget() =>
        new(RequestId, ActionLinkAction.Accept, DateTimeOffset.UtcNow.AddHours(-1));

    private static Mock<IUnitOfWork> UnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static async Task<IResult> SettleAsync(
        RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory> audit,
        ActionLinkTarget? target,
        RequestAggregate? request = null,
        bool requestMissing = false,
        bool projectMissing = false,
        string token = ActionLinkToken,
        Mock<IUnitOfWork>? unitOfWork = null,
        RecordingOutputCacheInvalidator? cache = null,
        Mock<INotificationSender>? notifications = null)
    {
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.FindActionLinkTargetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(target);

        var requests = new Mock<IRepository<RequestAggregate, RequestId>>();
        requests.Setup(r => r.FindAsync(It.IsAny<RequestId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(requestMissing ? null : request ?? SelfOnboardingRequest()));

        var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(BuildProject()));

        var users = new Mock<IRepository<Domain.User.User, IdpId>>();
        users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(Domain.User.User.CreateUser(
                Email.Create(RequesterEmail), UserName.Create(RequesterName), IdpId.Create(RequesterIdp))));

        var directory = new Mock<IUserDirectory>();
        directory.Setup(d => d.FindIdpIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>())).ReturnsAsync(IdpId.Create(RequesterIdp));

        var projectDirectory = new Mock<IProjectDirectory>();
        projectDirectory.Setup(d => d.FindShortNameAsync(It.IsAny<ProjectId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(projectMissing ? null : ShortName);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>())).ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));

        return await HandleActionLinkEndpoint.HandleAsync(
            new HandleActionLinkEndpoint.Request(token),
            query.Object, requests.Object, projects.Object, users.Object, directory.Object,
            projectDirectory.Object, roleList.Object,
            Options.Create(new WellKnownRoles()), (notifications ?? new Mock<INotificationSender>()).Object,
            (unitOfWork ?? UnitOfWork()).Object, cache ?? new RecordingOutputCacheInvalidator(),
            audit, new DefaultHttpContext(), CancellationToken.None);
    }

    private static PublicRequestSummary Summary(
        int requestState = 1,
        int requestType = 2,
        DateTimeOffset? validTo = null,
        string? slug = RequestSlug) => new(
        RequestId, RequesterName, RequesterEmail,
        ShortName, "res-1", "provider-1",
        requestState, validTo ?? DateTimeOffset.UtcNow.AddDays(1), requestType, ActionLinkToken, SiblingLinkToken,
        Slug: slug);

    private static async Task<IResult> PreviewAsync(
        RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory> audit,
        ActionLinkTarget? target,
        PublicRequestSummary? summary,
        string token = ActionLinkToken)
    {
        var query = new Mock<IRequestQuery>();
        query.Setup(q => q.FindActionLinkTargetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(target);
        query.Setup(q => q.GetPublicRequestSummaryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(summary);

        return await GetPublicRequestInfoEndpoint.HandleAsync(
            new GetPublicRequestInfoEndpoint.Request(token), query.Object, audit, CancellationToken.None);
    }

    private static ResourceRequestDetail ResourceDetail() => new(
        Slug: ResourceSlug,
        State: "PendingPiApproval",
        ProjectShortName: ResourceTenant,
        ProviderName: "it4i",
        ResourceName: "gpu",
        RequesterIdpId: ResourceRequesterIdp,
        ContactEmail: PiEmail,
        WorkflowRequestId: Guid.NewGuid(),
        Reason: null,
        TokenExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    private static Mock<IProjectResourceRequestQuery> ResourceQuery(ResourceRequestDetail? detail)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.FindByValidTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        return query;
    }

    private static Task<IResult> ViewResourceActionAsync(
        RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory> audit,
        ResourceRequestDetail? detail,
        string token = ResourceToken) =>
        GetResourceRequestActionEndpoint.HandleAsync(
            new GetResourceRequestActionEndpoint.Request(token), ResourceQuery(detail).Object, audit, CancellationToken.None);

    private static Task<IResult> SettleResourceActionAsync(
        RecordingAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory> audit,
        ResourceRequestDetail? detail,
        ResumeOutcome outcome = ResumeOutcome.Resumed,
        string decision = "Approve",
        string? reason = null,
        string token = ResourceToken)
    {
        var workflow = new Mock<IWorkflowClient>();
        workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(outcome));

        return HandleResourceRequestActionEndpoint.HandleAsync(
            new HandleResourceRequestActionEndpoint.Request(token, new HandleResourceRequestActionEndpoint.Body(decision, reason)),
            ResourceQuery(detail).Object, workflow.Object, audit, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    private static IReadOnlyList<string> DataValues(AuditEvent auditEvent) =>
        [.. (auditEvent.Data ?? new Dictionary<string, object?>()).Values
            .SelectMany(value => value is IEnumerable items and not string
                ? items.Cast<object?>()
                : [value])
            .Select(value => value?.ToString() ?? string.Empty)];

    private static IEnumerable<string> RecordedStrings(AuditEvent auditEvent) =>
        new[]
        {
            auditEvent.Action, auditEvent.ActorId, auditEvent.ActorType, auditEvent.TenantId,
            auditEvent.TargetType, auditEvent.TargetId, auditEvent.Reason, auditEvent.OnBehalfOfId,
        }
        .OfType<string>()
        .Concat((auditEvent.Data ?? new Dictionary<string, object?>()).Keys)
        .Concat(DataValues(auditEvent));

    private static void AssertDenied(AuditEvent auditEvent, string action, string reason, string? token)
    {
        Assert.Equal(action, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("anonymous", auditEvent.ActorId);
        Assert.Equal("action-link", auditEvent.ActorType);
        Assert.Equal(reason, auditEvent.Reason);
        Assert.Equal(["token_hash"], auditEvent.Data!.Keys);
        Assert.Equal(token is null ? null : ResourceRequestToken.Hash(token), auditEvent.Data["token_hash"]);
    }

    private static void AssertNoSecretOrPersonalData(IEnumerable<AuditEvent> events, params string[] forbidden)
    {
        var recorded = events.ToList();
        Assert.NotEmpty(recorded);
        foreach (var auditEvent in recorded)
        {
            Assert.True(auditEvent.Data?.ContainsKey("token_hash"), $"{auditEvent.Action} ({auditEvent.Outcome}) carries no token_hash");
            Assert.Null(auditEvent.OldValues);
            Assert.Null(auditEvent.NewValues);
            foreach (var value in RecordedStrings(auditEvent))
            {
                foreach (var secret in forbidden)
                    Assert.DoesNotContain(secret, value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Theory]
    [InlineData(TokenClass.Unknown, "request.not_found")]
    [InlineData(TokenClass.Expired, "request.expired")]
    [InlineData(TokenClass.Spent, "request.not_found")]
    public async Task Should_AnswerTheUniform400AndWriteOneDenied_When_SettlingWithADeadToken(TokenClass tokenClass, string reason)
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>();

        var result = await SettleAsync(audit, tokenClass == TokenClass.Expired ? ExpiredTarget() : null);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal("request.invalid_action_link", CodeOf(result));
        AssertDenied(Assert.Single(audit.Events), SettledAction, reason, ActionLinkToken);
    }

    [Theory]
    [InlineData(SettleCause.RequestMissing, "request.not_found")]
    [InlineData(SettleCause.RequestNotSelfOnboarding, "request.invalid_type")]
    [InlineData(SettleCause.ProjectMissing, "project.not_found")]
    [InlineData(SettleCause.ActionNeitherAcceptNorReject, "request.invalid_action_link")]
    public async Task Should_AnswerTheUniform400AndWriteOneDeniedWithItsOwnReason_When_ASettleCheckRefuses(SettleCause cause, string reason)
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>();

        var result = await SettleCauseAsync(audit, cause);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal("request.invalid_action_link", CodeOf(result));
        AssertDenied(Assert.Single(audit.Events), SettledAction, reason, ActionLinkToken);
    }

    private static Task<IResult> SettleCauseAsync(RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory> audit, SettleCause cause) => cause switch
    {
        SettleCause.RequestMissing => SettleAsync(audit, LiveTarget(), requestMissing: true),
        SettleCause.RequestNotSelfOnboarding => SettleAsync(audit, LiveTarget(), SelfOnboardingRequest(type: RequestType.ProjectResourceAssignment)),
        SettleCause.ProjectMissing => SettleAsync(audit, LiveTarget(), projectMissing: true),
        SettleCause.ActionNeitherAcceptNorReject => SettleAsync(audit, LiveTarget(action: 3)),
        _ => throw new ArgumentOutOfRangeException(nameof(cause)),
    };

    [Fact]
    public async Task Should_AnswerAByteIdenticalProblem_When_AnySettleCauseIsRefused()
    {
        var results = new List<ProblemHttpResult>();
        foreach (var cause in Enum.GetValues<SettleCause>())
            results.Add(Assert.IsType<ProblemHttpResult>(await SettleCauseAsync(new(), cause)));
        results.Add(Assert.IsType<ProblemHttpResult>(await SettleAsync(new(), null)));
        results.Add(Assert.IsType<ProblemHttpResult>(await SettleAsync(new(), ExpiredTarget())));

        var first = results[0];
        Assert.All(results, other =>
        {
            Assert.Equal(first.StatusCode, other.StatusCode);
            Assert.Equal(first.ProblemDetails.Title, other.ProblemDetails.Title);
            Assert.Equal(first.ProblemDetails.Detail, other.ProblemDetails.Detail);
            Assert.Equal(first.ProblemDetails.Extensions["code"], other.ProblemDetails.Extensions["code"]);
        });
    }

    [Theory]
    [InlineData(ActionLinkAction.Accept)]
    [InlineData(ActionLinkAction.Reject)]
    public async Task Should_WriteOneSuccessTargetingTheSlug_When_ALinkSettles(int action)
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>();

        var result = await SettleAsync(audit, LiveTarget(action));

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(SettledAction, auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("anonymous", auditEvent.ActorId);
        Assert.Equal("action-link", auditEvent.ActorType);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(RequestSlug, auditEvent.TargetId);
        Assert.Equal(["token_hash"], auditEvent.Data!.Keys);
        Assert.Equal(ResourceRequestToken.Hash(ActionLinkToken), auditEvent.Data["token_hash"]);
    }

    [Theory]
    [InlineData(ActionLinkAction.Accept)]
    [InlineData(ActionLinkAction.Reject)]
    public async Task Should_TargetTheRequestId_When_ALinkOfARequestWithoutSlugSettles(int action)
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>();

        await SettleAsync(audit, LiveTarget(action), SelfOnboardingRequest(slug: null));

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(RequestId.ToString(), auditEvent.TargetId);
    }

    [Fact]
    public async Task Should_CommitNotifyAndEvictBeforeThrowing_When_TheAcceptSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();
        var cache = new RecordingOutputCacheInvalidator();
        var notifications = new Mock<INotificationSender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SettleAsync(audit, LiveTarget(ActionLinkAction.Accept), unitOfWork: unitOfWork, cache: cache, notifications: notifications));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendRequestAcceptedToRequesterAsync(
            It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([CacheTags.Project(ShortName.Value), CacheTags.UserInfo(RequesterIdp)], cache.EvictedTags);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_CommitAndNotifyBeforeThrowing_When_TheRejectSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };
        var unitOfWork = UnitOfWork();
        var notifications = new Mock<INotificationSender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SettleAsync(audit, LiveTarget(ActionLinkAction.Reject), unitOfWork: unitOfWork, notifications: notifications));

        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.SendRequestRejectedToRequesterAsync(
            It.IsAny<Email>(), It.IsAny<Guid>(), It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Theory]
    [InlineData(ActionLinkAction.Accept)]
    [InlineData(ActionLinkAction.Reject)]
    public async Task Should_WriteNothing_When_TheSettleFailsInTheDomainAfterTheLinkResolved(int action)
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>();

        var result = await SettleAsync(audit, LiveTarget(action), SelfOnboardingRequest(rejected: true));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_KeepThePlaintextTokenAndPersonalDataOutOfEverySettleRecord()
    {
        var audit = new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>();

        await SettleAsync(audit, LiveTarget(ActionLinkAction.Accept));
        await SettleAsync(audit, LiveTarget(ActionLinkAction.Reject));
        await SettleAsync(audit, null);
        await SettleAsync(audit, ExpiredTarget());
        foreach (var cause in Enum.GetValues<SettleCause>())
            await SettleCauseAsync(audit, cause);

        Assert.Equal(8, audit.Events.Count);
        AssertNoSecretOrPersonalData(audit.Events, ActionLinkToken, RequesterEmail, RequesterName, "@");
    }

    [Theory]
    [InlineData(TokenClass.Unknown, "request.not_found")]
    [InlineData(TokenClass.Expired, "request.expired")]
    [InlineData(TokenClass.Spent, "request.not_found")]
    public async Task Should_AnswerTheSameCodeAndWriteOneDeniedWithIt_When_PreviewingWithADeadToken(TokenClass tokenClass, string code)
    {
        var audit = new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory>();

        var result = tokenClass == TokenClass.Expired
            ? await PreviewAsync(audit, LiveTarget(), Summary(validTo: DateTimeOffset.UtcNow.AddDays(-1)))
            : await PreviewAsync(audit, null, null);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(code, CodeOf(result));
        AssertDenied(Assert.Single(audit.Events), ViewedAction, code, ActionLinkToken);
    }

    [Theory]
    [InlineData(PreviewCause.SummaryMissing, "request.not_found")]
    [InlineData(PreviewCause.NotPending, "request.not_pending")]
    [InlineData(PreviewCause.NotSelfOnboardingType, "request.invalid_type")]
    public async Task Should_AnswerTheSameCodeAndWriteOneDeniedWithIt_When_APreviewCheckRefuses(PreviewCause cause, string code)
    {
        var audit = new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory>();

        var summary = cause switch
        {
            PreviewCause.SummaryMissing => null,
            PreviewCause.NotPending => Summary(requestState: 4),
            PreviewCause.NotSelfOnboardingType => Summary(requestType: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(cause)),
        };
        var result = await PreviewAsync(audit, LiveTarget(), summary);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Equal(code, CodeOf(result));
        AssertDenied(Assert.Single(audit.Events), ViewedAction, code, ActionLinkToken);
    }

    [Fact]
    public async Task Should_AnswerTheUnchangedBodyAndWriteOneSuccessTargetingTheSlug_When_APreviewSucceeds()
    {
        var audit = new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory>();

        var result = await PreviewAsync(audit, LiveTarget(), Summary());

        var ok = Assert.IsType<Ok<GetPublicRequestInfoEndpoint.Response>>(result);
        Assert.Equal(RequesterName, ok.Value!.RequestorUserName);
        Assert.Equal(RequesterEmail, ok.Value.RequestorEmail);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(ViewedAction, auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("anonymous", auditEvent.ActorId);
        Assert.Equal("action-link", auditEvent.ActorType);
        Assert.Equal(ShortName.Value, auditEvent.TenantId);
        Assert.Equal(RequestSlug, auditEvent.TargetId);
        Assert.Equal(["token_hash"], auditEvent.Data!.Keys);
        Assert.Equal(ResourceRequestToken.Hash(ActionLinkToken), auditEvent.Data["token_hash"]);
    }

    [Fact]
    public async Task Should_TargetTheRequestId_When_APreviewOfARequestWithoutSlugSucceeds()
    {
        var audit = new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory>();

        await PreviewAsync(audit, LiveTarget(), Summary(slug: null));

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal(RequestId.ToString(), auditEvent.TargetId);
    }

    [Fact]
    public async Task Should_Throw_When_ThePreviewSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };

        await Assert.ThrowsAsync<InvalidOperationException>(() => PreviewAsync(audit, LiveTarget(), Summary()));

        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_KeepThePlaintextTokensAndPersonalDataOutOfEveryPreviewRecord()
    {
        var audit = new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory>();

        await PreviewAsync(audit, LiveTarget(), Summary());
        await PreviewAsync(audit, null, null);
        await PreviewAsync(audit, LiveTarget(), Summary(validTo: DateTimeOffset.UtcNow.AddDays(-1)));
        await PreviewAsync(audit, LiveTarget(), Summary(requestState: 4));

        Assert.Equal(4, audit.Events.Count);
        AssertNoSecretOrPersonalData(audit.Events, ActionLinkToken, SiblingLinkToken, RequesterEmail, RequesterName, "@");
    }

    [Theory]
    [InlineData(TokenClass.Unknown)]
    [InlineData(TokenClass.Expired)]
    [InlineData(TokenClass.Spent)]
    public async Task Should_AnswerTheUniform404AndWriteOneDenied_When_PreviewingAResourceActionWithADeadToken(TokenClass tokenClass)
    {
        var audit = new RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory>();
        var token = $"{tokenClass}-{ResourceToken}";

        var result = await ViewResourceActionAsync(audit, detail: null, token);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal("resource_request.action_not_found", CodeOf(result));
        AssertDenied(Assert.Single(audit.Events), ResourceViewedAction, "resource_request.action_not_found", token);
    }

    [Fact]
    public async Task Should_WriteOneSuccessWithTheProjectTenantAndIdentifiers_When_AResourceActionPreviewSucceeds()
    {
        var audit = new RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory>();

        var result = await ViewResourceActionAsync(audit, ResourceDetail());

        Assert.IsType<Ok<GetResourceRequestActionEndpoint.Response>>(result);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(ResourceViewedAction, auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("anonymous", auditEvent.ActorId);
        Assert.Equal("action-link", auditEvent.ActorType);
        Assert.Equal(ResourceTenant, auditEvent.TenantId);
        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["token_hash"] = ResourceRequestToken.Hash(ResourceToken),
                ["slug"] = ResourceSlug,
                ["requester_idp_id"] = ResourceRequesterIdp,
            },
            auditEvent.Data!.OrderBy(kv => kv.Key).ToDictionary());
    }

    [Fact]
    public async Task Should_Throw_When_TheResourceActionPreviewSuccessWriteFails()
    {
        var audit = new RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory> { ThrowOn = AuditOutcome.Success };

        await Assert.ThrowsAsync<InvalidOperationException>(() => ViewResourceActionAsync(audit, ResourceDetail()));

        Assert.Equal(AuditOutcome.Success, Assert.Single(audit.Events).Outcome);
    }

    [Fact]
    public async Task Should_KeepThePlaintextTokenAndPersonalDataOutOfEveryResourceActionPreviewRecord()
    {
        var audit = new RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory>();

        await ViewResourceActionAsync(audit, ResourceDetail());
        await ViewResourceActionAsync(audit, detail: null);

        Assert.Equal(2, audit.Events.Count);
        AssertNoSecretOrPersonalData(audit.Events, ResourceToken, PiEmail, "@");
    }

    [Theory]
    [InlineData(TokenClass.Unknown)]
    [InlineData(TokenClass.Expired)]
    [InlineData(TokenClass.Spent)]
    public async Task Should_AnswerTheUniform404AndWriteOneDeniedWithTheActionNotFoundReason_When_SettlingAResourceActionWithADeadToken(TokenClass tokenClass)
    {
        var audit = new RecordingAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory>();
        var token = $"{tokenClass}-{ResourceToken}";

        var result = await SettleResourceActionAsync(audit, detail: null, token: token);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal("resource_request.action_not_found", CodeOf(result));
        AssertDenied(Assert.Single(audit.Events), ResourceSettledAction, "resource_request.action_not_found", token);
    }

    [Theory]
    [InlineData(ResumeOutcome.NotAwaiting)]
    [InlineData(ResumeOutcome.SignalTypeMismatch)]
    [InlineData(ResumeOutcome.RequestNotFound)]
    public async Task Should_AnswerTheUniform404AndWriteOneDeniedNotAwaitingDecision_When_TheEngineDoesNotResume(ResumeOutcome outcome)
    {
        var audit = new RecordingAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory>();

        var result = await SettleResourceActionAsync(audit, ResourceDetail(), outcome);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.Equal("resource_request.action_not_found", CodeOf(result));
        AssertDenied(Assert.Single(audit.Events), ResourceSettledAction, "resource_request.not_awaiting_decision", ResourceToken);
    }

    [Theory]
    [InlineData("Maybe", null)]
    [InlineData("Reject", "over-limit")]
    public async Task Should_WriteNothing_When_AResourceActionSettleIsAValidationFailure(string decision, string? reason)
    {
        var audit = new RecordingAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory>();
        var body = reason is null ? null : new string('x', ResourceRequestActionProblems.MaxReasonLength + 1);

        var result = await SettleResourceActionAsync(audit, ResourceDetail(), decision: decision, reason: body);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Empty(audit.Events);
    }

    [Fact]
    public async Task Should_KeepThePlaintextTokenPersonalDataAndReasonOutOfEveryResourceActionSettleRecord()
    {
        var audit = new RecordingAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory>();

        await SettleResourceActionAsync(audit, ResourceDetail(), decision: "Reject", reason: RejectReason);
        await SettleResourceActionAsync(audit, ResourceDetail(), ResumeOutcome.NotAwaiting, "Reject", RejectReason);
        await SettleResourceActionAsync(audit, detail: null, decision: "Reject", reason: RejectReason);

        Assert.Equal(3, audit.Events.Count);
        AssertNoSecretOrPersonalData(audit.Events, ResourceToken, PiEmail, RejectReason, "@");
    }

    [Theory]
    [InlineData(Route.SettleActionLink)]
    [InlineData(Route.PreviewActionLink)]
    [InlineData(Route.PreviewResourceAction)]
    [InlineData(Route.SettleResourceAction)]
    public async Task Should_WriteOneDeniedWithANullTokenHash_When_TheTokenIsBlank(Route route)
    {
        const string blank = " ";
        var (result, events) = route switch
        {
            Route.SettleActionLink => await Blank(new RecordingAuditLogger<HandleActionLinkEndpoint.AuditCategory>(), a => SettleAsync(a, null, token: blank)),
            Route.PreviewActionLink => await Blank(new RecordingAuditLogger<GetPublicRequestInfoEndpoint.AuditCategory>(), a => PreviewAsync(a, null, null, blank)),
            Route.PreviewResourceAction => await Blank(new RecordingAuditLogger<GetResourceRequestActionEndpoint.AuditCategory>(), a => ViewResourceActionAsync(a, null, blank)),
            Route.SettleResourceAction => await Blank(new RecordingAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory>(), a => SettleResourceActionAsync(a, null, token: blank)),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };

        Assert.True(StatusOf(result) is StatusCodes.Status400BadRequest or StatusCodes.Status404NotFound);
        var auditEvent = Assert.Single(events);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(["token_hash"], auditEvent.Data!.Keys);
        Assert.Null(auditEvent.Data["token_hash"]);
    }

    private static async Task<(IResult Result, IReadOnlyList<AuditEvent> Events)> Blank<TCategory>(
        RecordingAuditLogger<TCategory> audit, Func<RecordingAuditLogger<TCategory>, Task<IResult>> invoke) =>
        (await invoke(audit), audit.Events);
}
