using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Features.ResourceRequest;
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

public sealed class CancelResourceRequestEndpointCharacterizationTests
{
    private const string Project = "proj";
    private const string Slug = "proj-gpu";
    private static readonly Guid WorkflowId = Guid.CreateVersion7();

    private sealed class Sut
    {
        public string RouteShortName { get; set; } = Project;

        public ResourceRequestDetail? Detail { get; set; } = BuildDetail(ResourceRequestState.PendingPiApproval, WorkflowId);

        public ResumeOutcome Outcome { get; set; } = ResumeOutcome.Resumed;

        public Mock<IProjectResourceRequestQuery> Query { get; } = new();
        public Mock<IResourceRequestProjectionStore> Store { get; } = new();
        public Mock<IWorkflowClient> Workflow { get; } = new();

        public Sut()
        {
            Query.Setup(q => q.FindBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Detail);
            Workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<PiDecision>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ResumeResult(Outcome));
            Workflow.Setup(w => w.ResumeAsync(It.IsAny<Guid>(), It.IsAny<ResourceAvailabilitySignal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ResumeResult(Outcome));
        }

        public Task<IResult> InvokeAsync() =>
            CancelResourceRequestEndpoint.HandleAsync(
                new CancelResourceRequestEndpoint.Request(RouteShortName, Slug),
                new Caller(new IdpId("mgr"), "Manager", null,
                    new Dictionary<string, IReadOnlySet<ProjectShortName>>
                    {
                        ["Manager"] = new HashSet<ProjectShortName> { ProjectShortName.Create(Project) },
                    }),
                Query.Object, Store.Object, Workflow.Object, new RecordingAuditLogger<CancelResourceRequestEndpoint.AuditCategory>(), NullLogger<CancelResourceRequestEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static ResourceRequestDetail BuildDetail(ResourceRequestState state, Guid workflowId) => new(
        Slug, state.Value, Project, "acme", "gpu", "idp-1", "pi@example.org", workflowId, null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static void AssertProblem(IResult result, int status, string code, string detail)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    [Theory]
    [InlineData("PendingPiApproval")]
    [InlineData("AwaitingResourceCreation")]
    public async Task Should_SettleTheOrphanAsCancelledAndClearItsToken_When_ItsWorkflowNeverStarted(string state)
    {
        var sut = new Sut { Detail = BuildDetail(ResourceRequestState.From(state), Guid.Empty) };

        var result = await sut.InvokeAsync();

        Assert.IsType<NoContent>(result);
        var mutate = (Action<ProjectResourceRequest>)sut.Store.Invocations
            .Single(i => i.Method.Name == nameof(IResourceRequestProjectionStore.UpdateBySlugAsync)).Arguments[1];
        var row = new ProjectResourceRequest
        {
            State = ResourceRequestState.From(state),
            TokenHash = "hash",
            TokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        };
        mutate(row);
        Assert.Equal(ResourceRequestState.Cancelled, row.State);
        Assert.Null(row.TokenHash);
        Assert.Null(row.TokenExpiresAt);
        sut.Workflow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnNotCancellableWithoutSettling_When_AnOrphanIsAlreadyTerminal()
    {
        var sut = new Sut { Detail = BuildDetail(ResourceRequestState.Cancelled, Guid.Empty) };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, ResourceRequestErrorCodes.NotCancellable, "The request is no longer active.");
        sut.Store.VerifyNoOtherCalls();
        sut.Workflow.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Assigned")]
    [InlineData("Rejected")]
    [InlineData("Failed")]
    [InlineData("Expired")]
    public async Task Should_ReturnNotCancellableWithoutResuming_When_TheRequestIsTerminal(string state)
    {
        var sut = new Sut { Detail = BuildDetail(ResourceRequestState.From(state), WorkflowId) };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, ResourceRequestErrorCodes.NotCancellable, "The request is no longer active.");
        sut.Workflow.VerifyNoOtherCalls();
        sut.Store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ResumeOutcome.NotAwaiting)]
    [InlineData(ResumeOutcome.SignalTypeMismatch)]
    [InlineData(ResumeOutcome.RequestNotFound)]
    public async Task Should_ReturnNotCancellableYet_When_TheEngineDidNotResume(ResumeOutcome outcome)
    {
        var sut = new Sut { Detail = BuildDetail(ResourceRequestState.AwaitingResourceCreation, WorkflowId), Outcome = outcome };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, ResourceRequestErrorCodes.NotCancellableYet, "The request is not ready to be cancelled yet; retry shortly.");
        sut.Workflow.Verify(w => w.ResumeAsync(
            WorkflowId, It.Is<ResourceAvailabilitySignal>(s => s.Signal == ResourceAvailabilityKind.Cancel), It.IsAny<CancellationToken>()), Times.Once);
        sut.Store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutQuerying_When_TheRouteShortNameIsInvalid()
    {
        var sut = new Sut { RouteShortName = "" };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, ResourceRequestErrorCodes.NotFound, "Request not found.");
        sut.Query.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithoutSettling_When_AnOrphanBelongsToAnotherProject()
    {
        var sut = new Sut
        {
            Detail = BuildDetail(ResourceRequestState.PendingPiApproval, Guid.Empty) with { ProjectShortName = "other" },
        };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, ResourceRequestErrorCodes.NotFound, "Request not found.");
        sut.Store.VerifyNoOtherCalls();
    }
}
