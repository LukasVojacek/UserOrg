using Core.DataAccess.Queries.Contracts;
using Core.Features.ResourceRequest.Flow;
using Core.Features.ResourceRequest.HandleResourceRequestAction;
using Core.Features.ResourceRequest.PublicAction;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.UnitTests.ResourceRequests.PublicAction;

/// <summary>
/// Handler tests for the anonymous POST action-link settlement (ADR-0064): a live token delivers Approve/Reject to
/// the PiApproval gate (204); a bad decision is a 400; every non-resolvable or already-settled token is the same 404.
/// </summary>
public sealed class HandleResourceRequestActionEndpointTests
{
    private const string Token = "live-token-value";
    private static readonly Guid WorkflowRequestId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeAuditLogger : IAuditLogger<HandleResourceRequestActionEndpoint.AuditCategory>
    {
        private readonly List<AuditEvent> _events = [];

        internal IReadOnlyList<AuditEvent> Events => _events;

        public ValueTask AuditAsync(AuditEvent auditEvent)
        {
            _events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask AuditAsync(AuditEvent auditEvent, Action<ILogger> log) => AuditAsync(auditEvent);
    }

    private static ResourceRequestDetail Detail() => new(
        Slug: "acme-gpu",
        State: "PendingPiApproval",
        ProjectShortName: "acme",
        ProviderName: "it4i",
        ResourceName: "gpu",
        RequesterIdpId: "idp-123",
        ContactEmail: "pi@example.org",
        WorkflowRequestId: WorkflowRequestId,
        Reason: null,
        TokenExpiresAt: DateTimeOffset.UtcNow.AddHours(1),
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch);

    private static Mock<IProjectResourceRequestQuery> Query(ResourceRequestDetail? detail)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query
            .Setup(q => q.FindByValidTokenHashAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);
        return query;
    }

    private static Mock<IWorkflowClient> Workflow(ResumeOutcome outcome)
    {
        var workflow = new Mock<IWorkflowClient>();
        workflow
            .Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(outcome));
        return workflow;
    }

    private static Task<IResult> Handle(
        ResourceRequestDetail? detail,
        HandleResourceRequestActionEndpoint.Body body,
        Mock<IWorkflowClient> workflow,
        string token = Token,
        FakeAuditLogger? auditLogger = null) =>
        HandleResourceRequestActionEndpoint.HandleAsync(
            new HandleResourceRequestActionEndpoint.Request(token, body),
            Query(detail).Object,
            workflow.Object,
            auditLogger ?? new FakeAuditLogger(),
            CancellationToken.None);

    [Fact]
    public async Task Should_Return204AndResumeApprove_When_TokenLiveAndApproved()
    {
        var workflow = Workflow(ResumeOutcome.Resumed);

        var result = await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Approve"), workflow);

        Assert.IsType<NoContent>(result);
        workflow.Verify(
            w => w.ResumeAsync(
                WorkflowRequestId,
                It.Is<PiDecision>(d => d.Decision == PiDecisionKind.Approve && d.Reason == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_ThreadReasonIntoSignal_When_Rejected()
    {
        var workflow = Workflow(ResumeOutcome.Resumed);

        var result = await Handle(
            Detail(),
            new HandleResourceRequestActionEndpoint.Body("Reject", "Insufficient justification"),
            workflow);

        Assert.IsType<NoContent>(result);
        workflow.Verify(
            w => w.ResumeAsync(
                WorkflowRequestId,
                It.Is<PiDecision>(d => d.Decision == PiDecisionKind.Reject && d.Reason == "Insufficient justification"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Should_Return404AndNotResume_When_TokenUnknown()
    {
        var workflow = Workflow(ResumeOutcome.Resumed);

        var result = await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Approve"), workflow);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(ResourceRequestActionProblems.ActionNotFoundCode, problem.ProblemDetails.Extensions["code"]);
        workflow.Verify(
            w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Return404_When_EngineNoLongerAwaiting()
    {
        var workflow = Workflow(ResumeOutcome.NotAwaiting);

        var result = await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Approve"), workflow);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(ResourceRequestActionProblems.ActionNotFoundCode, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_Return400_When_DecisionInvalid()
    {
        var workflow = Workflow(ResumeOutcome.Resumed);

        // A valid token is required so the 400 cannot itself leak token validity.
        var result = await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Maybe"), workflow);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ResourceRequestActionProblems.InvalidDecisionCode, problem.ProblemDetails.Extensions["code"]);
        workflow.Verify(
            w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_Return400AndNotResume_When_ReasonOverLimit_WithValidToken()
    {
        var workflow = Workflow(ResumeOutcome.Resumed);
        var reason = new string('x', ResourceRequestActionProblems.MaxReasonLength + 1);

        // A valid token is required — the length check runs after the token resolve, so only a token holder sees this 400.
        var result = await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Reject", reason), workflow);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ResourceRequestActionProblems.ReasonTooLongCode, problem.ProblemDetails.Extensions["code"]);
        workflow.Verify(
            w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_ReasonOverLimit_WithInvalidToken()
    {
        // An over-limit reason on an INVALID token must still 404 identically — the length 400 must never leak that
        // the token was invalid (ADR-0064 anti-leak: token resolve happens first).
        var reason = new string('x', ResourceRequestActionProblems.MaxReasonLength + 1);
        var unknown = Assert.IsType<ProblemHttpResult>(
            await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed)));
        var overLimit = Assert.IsType<ProblemHttpResult>(
            await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Reject", reason), Workflow(ResumeOutcome.Resumed)));

        Assert.Equal(unknown.StatusCode, overLimit.StatusCode);
        Assert.Equal(unknown.ProblemDetails.Detail, overLimit.ProblemDetails.Detail);
        Assert.Equal(unknown.ProblemDetails.Extensions["code"], overLimit.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_Across404Causes()
    {
        // Unknown / expired / used tokens (null lookup) and an already-settled request (engine not awaiting) must
        // all produce a byte-identical 404 — no distinguishable cause.
        var unknown = Assert.IsType<ProblemHttpResult>(
            await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed)));
        var used = Assert.IsType<ProblemHttpResult>(
            await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed), token: "other"));
        var settled = Assert.IsType<ProblemHttpResult>(
            await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.NotAwaiting)));

        foreach (var other in new[] { used, settled })
        {
            Assert.Equal(unknown.StatusCode, other.StatusCode);
            Assert.Equal(unknown.ProblemDetails.Detail, other.ProblemDetails.Detail);
            Assert.Equal(unknown.ProblemDetails.Extensions["code"], other.ProblemDetails.Extensions["code"]);
        }
    }

    [Fact]
    public async Task Should_WriteSuccessAuditEvent_When_ResumeSucceeds()
    {
        var auditLogger = new FakeAuditLogger();

        await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed), auditLogger: auditLogger);

        var auditEvent = Assert.Single(auditLogger.Events);
        Assert.Equal(AuditActions.ResourceRequestActionSettled, auditEvent.Action);
        Assert.Equal(AuditOutcome.Success, auditEvent.Outcome);
        Assert.Equal("anonymous", auditEvent.ActorId);
        Assert.Equal("action-link", auditEvent.ActorType);
        Assert.Equal("acme", auditEvent.TenantId);
        Assert.Equal("acme-gpu", auditEvent.Data!["slug"]);
        Assert.Equal("idp-123", auditEvent.Data!["requester_idp_id"]);
        Assert.Equal(ResourceRequestToken.Hash(Token), auditEvent.Data!["token_hash"]);
    }

    [Fact]
    public async Task Should_WriteDeniedAuditEvent_When_TokenUnresolved()
    {
        var auditLogger = new FakeAuditLogger();

        await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed), auditLogger: auditLogger);

        var auditEvent = Assert.Single(auditLogger.Events);
        Assert.Equal(AuditActions.ResourceRequestActionSettled, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("anonymous", auditEvent.ActorId);
        Assert.Equal("action-link", auditEvent.ActorType);
        Assert.Equal(ResourceRequestToken.Hash(Token), auditEvent.Data!["token_hash"]);
    }

    [Fact]
    public async Task Should_UseSnakeCaseDataKeys_When_AuditingEitherOutcome()
    {
        // The keys are persisted to an append-only store: renaming one later forces every historical query to know
        // both spellings forever, so the snake_case convention is asserted, not merely followed.
        var successLogger = new FakeAuditLogger();
        var deniedLogger = new FakeAuditLogger();

        await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed), auditLogger: successLogger);
        await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed), auditLogger: deniedLogger);

        var keys = successLogger.Events.Concat(deniedLogger.Events).SelectMany(e => e.Data!.Keys);
        Assert.All(keys, key => Assert.Matches("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", key));
    }

    [Fact]
    public async Task Should_NotContainPlaintextTokenOrEmail_When_AuditingEitherOutcome()
    {
        var successLogger = new FakeAuditLogger();
        var deniedLogger = new FakeAuditLogger();

        await Handle(Detail(), new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed), auditLogger: successLogger);
        await Handle(detail: null, new HandleResourceRequestActionEndpoint.Body("Approve"), Workflow(ResumeOutcome.Resumed), auditLogger: deniedLogger);

        var contactEmail = Detail().ContactEmail;
        foreach (var auditEvent in successLogger.Events.Concat(deniedLogger.Events))
        {
            // Non-empty, so the absence assertions below cannot pass vacuously.
            Assert.NotEmpty(auditEvent.Data!);

            // Substring, not equality: the token is functionally a password, so it must not appear even embedded
            // inside a larger value (a message, a URL) or inside a key.
            foreach (var entry in auditEvent.Data!)
            {
                Assert.DoesNotContain(Token, entry.Key, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(contactEmail, entry.Key, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("email", entry.Key, StringComparison.OrdinalIgnoreCase);

                var rendered = entry.Value?.ToString() ?? string.Empty;
                Assert.DoesNotContain(Token, rendered, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(contactEmail, rendered, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
