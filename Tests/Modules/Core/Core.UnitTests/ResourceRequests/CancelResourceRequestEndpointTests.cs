using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Features.ResourceRequest.CancelResourceRequest;
using Core.Features.ResourceRequest.Flow;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Unit tests for <see cref="CancelResourceRequestEndpoint"/> (route 5, ADR-0064): a cancel from each gate state
/// resumes the matching signal type; terminal states → 409; and the anti-leak 404 discipline (missing /
/// out-of-scope / wrong-project all identical, ADR-0018).
/// </summary>
public sealed class CancelResourceRequestEndpointTests
{
    private const string Project = "proj";
    private const string Slug = "proj-gpu";
    private static readonly Guid WorkflowId = Guid.CreateVersion7();

    private static Caller InScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>
        {
            ["Manager"] = new HashSet<ProjectShortName> { ProjectShortName.Create(Project) },
        });

    private static Caller OutOfScope() => new(
        new IdpId("mgr"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ResourceRequestDetail Detail(
        ResourceRequestState state, string project = Project, Guid? workflowId = null) => new(
        Slug, state.Value, project, "acme", "gpu", "idp-1", "pi@example.org", workflowId ?? WorkflowId, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static async Task<(IResult Result, Mock<IWorkflowClient> Workflow, Mock<IResourceRequestProjectionStore> Store)> InvokeAsync(
        Caller caller, ResourceRequestDetail? detail, ResumeOutcome outcome = ResumeOutcome.Resumed)
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.FindBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(detail);

        var workflow = new Mock<IWorkflowClient>();
        workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(outcome));
        workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResumeResult(outcome));

        var store = new Mock<IResourceRequestProjectionStore>();

        var result = await CancelResourceRequestEndpoint.HandleAsync(
            new CancelResourceRequestEndpoint.Request(Project, Slug), caller, query.Object, store.Object,
            workflow.Object,
            new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>(), NullLogger<CancelResourceRequestEndpoint.AuditCategory>.Instance, CancellationToken.None);
        return (result, workflow, store);
    }

    [Fact]
    public async Task Should_SettleRow_When_WorkflowNeverStarted()
    {
        // Orphan row (no workflow id): resuming is impossible, so cancel settles it instead of looping 409.
        var (result, workflow, store) = await InvokeAsync(
            InScope(), Detail(ResourceRequestState.PendingPiApproval, workflowId: Guid.Empty));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        store.Verify(s => s.UpdateBySlugAsync(Slug, It.IsAny<Action<ProjectResourceRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
        workflow.Verify(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ResumePiDecisionCancel_When_PendingPiApproval()
    {
        var (result, workflow, _) = await InvokeAsync(InScope(), Detail(ResourceRequestState.PendingPiApproval));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        workflow.Verify(w => w.ResumeAsync(
            WorkflowId, It.Is<PiDecision>(d => d.Decision == PiDecisionKind.Cancel), It.IsAny<CancellationToken>()), Times.Once);
        workflow.Verify(w => w.ResumeAsync(
            It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ResumeResourceAvailabilityCancel_When_AwaitingResourceCreation()
    {
        var (result, workflow, _) = await InvokeAsync(InScope(), Detail(ResourceRequestState.AwaitingResourceCreation));

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        workflow.Verify(w => w.ResumeAsync(
            WorkflowId, It.Is<ResourceAvailabilitySignal>(s => s.Signal == ResourceAvailabilityKind.Cancel), It.IsAny<CancellationToken>()), Times.Once);
        workflow.Verify(w => w.ResumeAsync(
            It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Assigned")]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    [InlineData("Failed")]
    public async Task Should_ReturnConflict_When_TerminalState(string state)
    {
        var (result, _, _) = await InvokeAsync(InScope(), Detail(ResourceRequestState.From(state)));

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_EngineNotAwaitingYet()
    {
        var (result, _, _) = await InvokeAsync(
            InScope(), Detail(ResourceRequestState.PendingPiApproval), ResumeOutcome.NotAwaiting);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("resource_request.not_cancellable_yet", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnIdenticalNotFound_When_MissingOutOfScopeOrWrongProject()
    {
        var (missing, _, _) = await InvokeAsync(InScope(), detail: null);
        var (outOfScope, _, _) = await InvokeAsync(OutOfScope(), Detail(ResourceRequestState.PendingPiApproval));
        var (wrongProject, _, _) = await InvokeAsync(InScope(), Detail(ResourceRequestState.PendingPiApproval, project: "other"));

        var notThere = Assert.IsType<ProblemHttpResult>(missing);
        var hidden = Assert.IsType<ProblemHttpResult>(outOfScope);
        var mismatched = Assert.IsType<ProblemHttpResult>(wrongProject);

        Assert.Equal(StatusCodes.Status404NotFound, notThere.StatusCode);
        foreach (var other in new[] { hidden, mismatched })
        {
            Assert.Equal(notThere.StatusCode, other.StatusCode);
            Assert.Equal(notThere.ProblemDetails.Extensions["code"], other.ProblemDetails.Extensions["code"]);
            Assert.Equal(notThere.ProblemDetails.Detail, other.ProblemDetails.Detail);
        }
    }
}
