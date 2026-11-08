using System.Collections.Immutable;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.Application;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Application;

public sealed class WorkflowClientTests
{
    private sealed record StepA(Guid RequestId) : StepRequest(RequestId, nameof(StepA));
    private sealed record StepB(Guid RequestId, bool Accept) : StepRequest(RequestId, nameof(StepB));
    private sealed record Approval(bool Accept);
    private sealed record OtherSignal(int Value);

    private sealed class NoopHandler<T> : IStepHandler<T> where T : StepRequest
    {
        public Task<StepResult> HandleAsync(T request, CancellationToken ct) => Task.FromResult(new StepResult(null));
    }

    // A -> Gate(ApprovalGate) -> (Accept ? B : terminal); B terminal
    private sealed class GatedFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named("gated")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Gate<Approval>("ApprovalGate")
            .From<StepA>().GoToGate("ApprovalGate")
            .FromGate<Approval>("ApprovalGate").Branch(c => c
                .When(a => a.Accept, (a, snap) => new StepB(snap.RequestId, true))
                .OtherwiseTerminal())
            .From<StepB>().Terminal();
    }

    private readonly IRequestApplicationService _appService = Substitute.For<IRequestApplicationService>();
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly IStrategyRegistry _registry = Substitute.For<IStrategyRegistry>();
    private readonly IQueuePort _queue = Substitute.For<IQueuePort>();
    private readonly ICallbackPort _callback = Substitute.For<ICallbackPort>();

    private static IRequestStrategy BuildGatedStrategy()
    {
        var services = new ServiceCollection();
        services.AddFlow<GatedFlow>();
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IRequestStrategy>().Single(s => s.Name == "gated");
    }

    private WorkflowClient CreateClient(IEnumerable<IFlowDefinition>? flows = null)
        => new(_appService, _repository, _registry, _queue, _callback, flows ?? [], NullLogger<WorkflowClient>.Instance);

    private RequestAggregate ParkAtGate(Guid id)
    {
        var aggregate = RequestAggregateBuilder.Build(
            requestId: id, strategy: "gated", status: RequestStatus.AwaitingSignal, currentState: "ApprovalGate");
        _repository.FindByIdAsync(id, Arg.Any<CancellationToken>()).Returns(aggregate);
        _registry.Resolve("gated").Returns(BuildGatedStrategy());
        return aggregate;
    }

    [Fact(DisplayName = "StartAsync builds the initial step from the flow and submits it")]
    public async Task Should_SubmitInitialStep_When_StartAsyncCalled()
    {
        _appService.SubmitAsync(Arg.Any<StepRequest>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new SubmitResponse(((StepRequest)ci[0]).RequestId, RequestStatus.Accepted));
        var client = CreateClient([new GatedFlow()]);

        var id = await client.StartAsync<GatedFlow>(new object());

        Assert.NotEqual(Guid.Empty, id);
        await _appService.Received(1).SubmitAsync(
            Arg.Is<StepRequest>(s => s is StepA), null, null, null, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "StartAsync throws when the flow is not registered")]
    public async Task Should_Throw_When_FlowNotRegistered()
    {
        var client = CreateClient([]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync<GatedFlow>(new object()));
    }

    [Fact(DisplayName = "ResumeAsync routes the gate and enqueues the next step")]
    public async Task Should_ResumeAndEnqueue_When_SignalAccepts()
    {
        var id = Guid.NewGuid();
        var aggregate = ParkAtGate(id);
        var client = CreateClient();

        var result = await client.ResumeAsync(id, new Approval(Accept: true));

        Assert.Equal(ResumeOutcome.Resumed, result.Outcome);
        Assert.Equal(RequestStatus.InProgress, aggregate.Status);
        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(Arg.Is<StepRequest>(s => s is StepB), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ResumeAsync completes the request when the gate routes to terminal")]
    public async Task Should_Complete_When_SignalTerminates()
    {
        var id = Guid.NewGuid();
        var aggregate = ParkAtGate(id);
        var client = CreateClient();

        var result = await client.ResumeAsync(id, new Approval(Accept: false));

        Assert.Equal(ResumeOutcome.Resumed, result.Outcome);
        Assert.Equal(RequestStatus.Completed, aggregate.Status);
        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ResumeAsync returns RequestNotFound for an unknown request")]
    public async Task Should_ReturnNotFound_When_RequestMissing()
    {
        var id = Guid.NewGuid();
        _repository.FindByIdAsync(id, Arg.Any<CancellationToken>()).Returns((RequestAggregate?)null);
        var client = CreateClient();

        var result = await client.ResumeAsync(id, new Approval(true));

        Assert.Equal(ResumeOutcome.RequestNotFound, result.Outcome);
    }

    [Fact(DisplayName = "ResumeAsync returns NotAwaiting when the request is not parked")]
    public async Task Should_ReturnNotAwaiting_When_NotParked()
    {
        var id = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: id, strategy: "gated", status: RequestStatus.InProgress);
        _repository.FindByIdAsync(id, Arg.Any<CancellationToken>()).Returns(aggregate);
        var client = CreateClient();

        var result = await client.ResumeAsync(id, new Approval(true));

        Assert.Equal(ResumeOutcome.NotAwaiting, result.Outcome);
        await _repository.DidNotReceive().SaveAsync(Arg.Any<RequestAggregate>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "ResumeAsync returns SignalTypeMismatch for the wrong signal type")]
    public async Task Should_ReturnMismatch_When_SignalTypeWrong()
    {
        var id = Guid.NewGuid();
        ParkAtGate(id);
        var client = CreateClient();

        var result = await client.ResumeAsync(id, new OtherSignal(1));

        Assert.Equal(ResumeOutcome.SignalTypeMismatch, result.Outcome);
        Assert.Equal(nameof(Approval), result.AwaitedSignalType);
    }

    [Fact(DisplayName = "RetryAsync returns Requeued on success")]
    public async Task Should_ReturnRequeued_When_RetrySucceeds()
    {
        var result = await CreateClient().RetryAsync(Guid.NewGuid());

        Assert.Equal(RetryOutcome.Requeued, result.Outcome);
    }

    [Fact(DisplayName = "RetryAsync maps KeyNotFound to RequestNotFound")]
    public async Task Should_ReturnNotFound_When_RetryThrowsKeyNotFound()
    {
        var id = Guid.NewGuid();
        _appService.RetryLastStepAsync(id, Arg.Any<CancellationToken>()).Returns(Task.FromException(new KeyNotFoundException()));

        var result = await CreateClient().RetryAsync(id);

        Assert.Equal(RetryOutcome.RequestNotFound, result.Outcome);
    }

    [Fact(DisplayName = "RetryAsync maps InvalidOperation to NotFailed")]
    public async Task Should_ReturnNotFailed_When_RetryThrowsInvalidOperation()
    {
        var id = Guid.NewGuid();
        _appService.RetryLastStepAsync(id, Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException()));

        var result = await CreateClient().RetryAsync(id);

        Assert.Equal(RetryOutcome.NotFailed, result.Outcome);
    }

    [Fact(DisplayName = "GetStatusAsync returns the response status")]
    public async Task Should_ReturnStatus_When_RequestExists()
    {
        var id = Guid.NewGuid();
        _appService.GetStatusAsync(id, Arg.Any<CancellationToken>()).Returns(
            new StatusResponse(id, null, "gated", RequestStatus.AwaitingSignal, "ApprovalGate",
                ImmutableList<StepStatusItem>.Empty, null, null, null,
                DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null));

        var status = await CreateClient().GetStatusAsync(id);

        Assert.Equal(RequestStatus.AwaitingSignal, status);
    }

    [Fact(DisplayName = "GetStatusAsync returns null when the request does not exist")]
    public async Task Should_ReturnNull_When_RequestMissing()
    {
        var id = Guid.NewGuid();
        _appService.GetStatusAsync(id, Arg.Any<CancellationToken>()).Returns((StatusResponse?)null);

        var status = await CreateClient().GetStatusAsync(id);

        Assert.Null(status);
    }
}
