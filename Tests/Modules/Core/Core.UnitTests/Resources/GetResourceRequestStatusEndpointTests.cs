using Core.Features.Resource.GetResourceRequestStatus;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.UnitTests.Resources;

/// <summary>
/// Handler tests for the async resource-request status poll endpoint (ADR-0057): projects
/// <see cref="StatusResponse"/> to a minimal flow-status + per-step summary, and 404s an unknown id.
/// </summary>
public sealed class GetResourceRequestStatusEndpointTests
{
    private static readonly Guid KnownId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static StatusResponse Status(Guid id) => new(
        RequestId: id,
        ClientId: "svc",
        Strategy: "CreateResource",
        Status: RequestStatus.InProgress,
        CurrentState: "CheckHeappe",
        Steps:
        [
            new StepStatusItem("CheckHeappe", StepExecutionStatus.Completed, 0, null, null, null, null, null),
            new StepStatusItem("InitializeHeappe", StepExecutionStatus.Running, 0, null, null, null, null, null),
        ],
        Result: null,
        ErrorCode: null,
        ErrorMessage: null,
        CreatedAt: DateTimeOffset.UnixEpoch,
        UpdatedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: null);

    private static Mock<IRequestApplicationService> Service(StatusResponse? status)
    {
        var svc = new Mock<IRequestApplicationService>();
        svc.Setup(s => s.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        return svc;
    }

    [Fact]
    public async Task Should_ReturnFlowStatusAndStepSummary_When_RequestKnown()
    {
        var result = await GetResourceRequestStatusEndpoint.HandleAsync(
            new GetResourceRequestStatusEndpoint.Request(KnownId),
            Service(Status(KnownId)).Object,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<GetResourceRequestStatusEndpoint.Response>>(result);
        Assert.Equal(KnownId, ok.Value!.RequestId);
        Assert.Equal(RequestStatus.InProgress, ok.Value.Status);
        Assert.Collection(
            ok.Value.Steps,
            s => Assert.Equal(("CheckHeappe", StepExecutionStatus.Completed), (s.StateName, s.ExecutionStatus)),
            s => Assert.Equal(("InitializeHeappe", StepExecutionStatus.Running), (s.StateName, s.ExecutionStatus)));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_RequestUnknown()
    {
        var result = await GetResourceRequestStatusEndpoint.HandleAsync(
            new GetResourceRequestStatusEndpoint.Request(Guid.NewGuid()),
            Service(null).Object,
            CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }
}
