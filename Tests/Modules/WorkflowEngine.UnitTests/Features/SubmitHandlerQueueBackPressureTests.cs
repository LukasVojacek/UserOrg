using Microsoft.Extensions.Logging;

using NSubstitute;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Adapters;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Features.Submit;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Features;

/// <summary>
/// The reported symptom in its own words: <c>POST providers/{providerName}/resources</c> never returned.
/// <see cref="SubmitHandler"/> enqueues on the HTTP request thread, so a queue that waits for room turns a
/// busy worker into a hung caller. Every repository/registry double here is synchronous, so the whole
/// <c>HandleAsync</c> pipeline completes synchronously unless something inside it genuinely suspends —
/// which makes <c>Task.IsCompleted</c> a deterministic "did the caller block?" signal with no wall clock
/// involved. <see cref="Should_CompleteWithoutSuspendingTheCaller_When_TheQueueHasCapacity"/> is the
/// control that keeps that claim honest.
/// </summary>
public sealed class SubmitHandlerQueueBackPressureTests
{
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly IStrategyRegistry _registry = Substitute.For<IStrategyRegistry>();
    private readonly IRequestStrategy _strategy = Substitute.For<IRequestStrategy>();

    public SubmitHandlerQueueBackPressureTests()
    {
        _strategy.Name.Returns("test-strategy");
        _strategy.InitialStepType.Returns(typeof(TestStepRequest));
        _registry.ResolveByStepType(typeof(TestStepRequest)).Returns(_strategy);
    }

    private SubmitHandler CreateHandler(IQueuePort queue)
        => new(_repository, queue, _registry, new SilentLogger<SubmitHandler>());

    private static TestStepRequest MakeStep() => new(Guid.NewGuid(), "init");

    [Fact(DisplayName = "Submitting completes without suspending the caller while the queue has room")]
    public async Task Should_CompleteWithoutSuspendingTheCaller_When_TheQueueHasCapacity()
    {
        var queue = new ChannelQueueAdapter(2);
        var handler = CreateHandler(queue);

        var submit = handler.HandleAsync(
            MakeStep(), clientId: "c1", callbackUrl: null, idempotencyKey: null,
            ct: TestContext.Current.CancellationToken);

        Assert.True(
            submit.IsCompleted,
            "With synchronous doubles and a queue that has room, submitting must not suspend the caller.");

        var response = await submit;
        Assert.Equal(RequestStatus.Accepted, response.Status);
    }

    [Fact(DisplayName = "Submitting returns to the caller even when the queue is full")]
    public async Task Should_NotBlockTheSubmitCaller_When_TheQueueIsFull()
    {
        var queue = new ChannelQueueAdapter(1);
        var handler = CreateHandler(queue);

        // Nothing consumes: the queue is at capacity for the rest of the test, exactly as it is when the
        // single RequestWorker sits inside a stalled HEAppE call.
        await queue.EnqueueAsync(MakeStep(), TestContext.Current.CancellationToken);

        // Deliberately not awaited before the assert: awaiting a blocked submit would hang the suite.
        var submit = handler.HandleAsync(
            MakeStep(), clientId: "c1", callbackUrl: null, idempotencyKey: null,
            ct: TestContext.Current.CancellationToken);

        Assert.True(
            submit.IsCompleted,
            "SubmitHandler must return to its HTTP caller when the queue is full. It is still pending, i.e. the request thread is parked on the channel writer — the reported 'the caller never returned'.");

        var response = await submit;
        Assert.Equal(RequestStatus.Accepted, response.Status);
        await _repository.Received(1).AddAsync(Arg.Any<RequestAggregate>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "The request is persisted and Accepted is returned even when the queue drops the step")]
    public async Task Should_PersistTheRequestAndReturnAccepted_When_TheQueueDropsTheStep()
    {
        // The workflow.Requests row is the durability boundary (ADR-0100); the queue is best-effort and
        // startup recovery re-enqueues what was lost. A dropped step must therefore not change the answer.
        var handler = CreateHandler(new DroppingQueue());
        var step = MakeStep();

        var response = await handler.HandleAsync(
            step, clientId: "c1", callbackUrl: null, idempotencyKey: null,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(step.RequestId, response.RequestId);
        Assert.Equal(RequestStatus.Accepted, response.Status);
        await _repository.Received(1).AddAsync(
            Arg.Is<RequestAggregate>(a => a.RequestId == step.RequestId && a.Status == RequestStatus.Accepted),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A queue that accepts nothing and never blocks — "the message was dropped".</summary>
    private sealed class DroppingQueue : IQueuePort
    {
        public ValueTask EnqueueAsync(StepRequest message, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public IAsyncEnumerable<StepRequest> ConsumeAsync(CancellationToken ct)
            => AsyncEnumerable.Empty<StepRequest>();
    }

    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
