using System.Text.Json;
using NSubstitute;
using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Features.Retry;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace WorkflowEngine.Core.Tests.Features;

public sealed class RetryLastStepHandlerTests
{
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly IQueuePort _queue = Substitute.For<IQueuePort>();

    private static ILogger<RetryHandler> CreateNullLogger() => new NullLogger();

    private RetryHandler CreateHandler() => new(_repository, _queue, CreateNullLogger());

    private static RequestStep BuildRetryableStep(
        Guid requestId,
        StepExecutionStatus status = StepExecutionStatus.Failed,
        DateTimeOffset? lockedUntil = null)
    {
        return RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            stepName: "step-1",
            executionStatus: status,
            messageType: typeof(TestStepRequest).AssemblyQualifiedName!,
            inputData: JsonDocument.Parse(
                $$$"""{"RequestId":"{{{Guid.Empty}}}","StateName":"step-1"}"""),
            lockedUntil: lockedUntil);
    }

    [Fact(DisplayName = "Constructor throws ArgumentNullException when repository is null")]
    public void Should_ThrowArgumentNullException_When_RepositoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RetryHandler(null!, _queue, CreateNullLogger()));
    }

    [Fact(DisplayName = "Constructor throws ArgumentNullException when queue is null")]
    public void Should_ThrowArgumentNullException_When_QueueIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RetryHandler(_repository, null!, CreateNullLogger()));
    }

    private sealed class NullLogger : ILogger<RetryHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Fact(DisplayName = "HandleAsync throws KeyNotFoundException when request is not found")]
    public async Task Should_ThrowKeyNotFoundException_When_RequestNotFound()
    {
        var requestId = Guid.NewGuid();
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>())
            .Returns((RequestAggregate?)null);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.RetryLastStepAsync(requestId, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when aggregate status is Accepted")]
    public async Task Should_ThrowInvalidOperationException_When_StatusIsAccepted()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.Accepted);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryLastStepAsync(requestId, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when aggregate status is Completed")]
    public async Task Should_ThrowInvalidOperationException_When_StatusIsCompleted()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.Completed);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryLastStepAsync(requestId, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when there are no steps")]
    public async Task Should_ThrowInvalidOperationException_When_NoStepsExist()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.InProgress);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryLastStepAsync(requestId, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when last step is Running")]
    public async Task Should_ThrowInvalidOperationException_When_LastStepIsRunning()
    {
        var requestId = Guid.NewGuid();
        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            executionStatus: StepExecutionStatus.Running);
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryLastStepAsync(requestId, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when last step is currently locked")]
    public async Task Should_ThrowInvalidOperationException_When_LastStepIsLocked()
    {
        var requestId = Guid.NewGuid();
        var lockedStep = BuildRetryableStep(
            requestId,
            status: StepExecutionStatus.Failed,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.Failed, steps: [lockedStep]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryLastStepAsync(requestId, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync resets step, saves, and enqueues when last step is Failed")]
    public async Task Should_ResetSaveAndEnqueue_When_LastStepIsFailed()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(requestId, StepExecutionStatus.Failed);
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.Failed, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await handler.RetryLastStepAsync(requestId, CancellationToken.None);

        Assert.Equal(StepExecutionStatus.Running, step.ExecutionStatus);
        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync resets step, saves, and enqueues when last step is RetryScheduled")]
    public async Task Should_ResetSaveAndEnqueue_When_LastStepIsRetryScheduled()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(requestId, StepExecutionStatus.RetryScheduled);
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await handler.RetryLastStepAsync(requestId, CancellationToken.None);

        Assert.Equal(StepExecutionStatus.Running, step.ExecutionStatus);
        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when MessageType cannot be resolved")]
    public async Task Should_ThrowInvalidOperationException_When_MessageTypeCannotBeResolved()
    {
        var requestId = Guid.NewGuid();
        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            executionStatus: StepExecutionStatus.Failed,
            messageType: "NonExistent.Type, NonExistentAssembly");
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryLastStepAsync(requestId, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync allows retry when LockedUntil is in the past")]
    public async Task Should_AllowRetry_When_LockedUntilIsInThePast()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(
            requestId,
            status: StepExecutionStatus.Failed,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(-1));
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.Failed, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await handler.RetryLastStepAsync(requestId, CancellationToken.None);

        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }
}
