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

public sealed class RetryStepHandlerTests
{
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly IQueuePort _queue = Substitute.For<IQueuePort>();

    private static ILogger<RetryHandler> CreateNullLogger() => new NullLogger();

    private RetryHandler CreateHandler() => new(_repository, _queue, CreateNullLogger());

    private static RequestStep BuildRetryableStep(
        Guid requestId,
        string stepName = "target-step",
        StepExecutionStatus status = StepExecutionStatus.Failed,
        DateTimeOffset? lockedUntil = null)
    {
        return RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            stepName: stepName,
            executionStatus: status,
            messageType: typeof(TestStepRequest).AssemblyQualifiedName!,
            inputData: JsonDocument.Parse(
                $$$"""{"RequestId":"{{{Guid.Empty}}}","StateName":"{{{stepName}}}"}"""),
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

    [Fact(DisplayName = "HandleAsync throws ArgumentNullException when stateName is null")]
    public async Task Should_ThrowArgumentException_When_StateNameIsNull()
    {
        var handler = CreateHandler();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => handler.RetryStepAsync(Guid.NewGuid(), null!, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws ArgumentException when stateName is whitespace")]
    public async Task Should_ThrowArgumentException_When_StateNameIsWhitespace()
    {
        var handler = CreateHandler();

        await Assert.ThrowsAsync<ArgumentException>(
            () => handler.RetryStepAsync(Guid.NewGuid(), "   ", CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws KeyNotFoundException when request is not found")]
    public async Task Should_ThrowKeyNotFoundException_When_RequestNotFound()
    {
        var requestId = Guid.NewGuid();
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>())
            .Returns((RequestAggregate?)null);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.RetryStepAsync(requestId, "step-x", CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when aggregate status is Accepted")]
    public async Task Should_ThrowInvalidOperationException_When_StatusIsAccepted()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.Accepted);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryStepAsync(requestId, "step-x", CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when aggregate status is Completed")]
    public async Task Should_ThrowInvalidOperationException_When_StatusIsCompleted()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.Completed);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryStepAsync(requestId, "step-x", CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws KeyNotFoundException when the named step is not found")]
    public async Task Should_ThrowKeyNotFoundException_When_NamedStepNotFound()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(requestId, stepName: "other-step");
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => handler.RetryStepAsync(requestId, "target-step", CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when named step is Running")]
    public async Task Should_ThrowInvalidOperationException_When_NamedStepIsRunning()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(requestId, "target-step", StepExecutionStatus.Running);
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryStepAsync(requestId, "target-step", CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync throws InvalidOperationException when named step is locked")]
    public async Task Should_ThrowInvalidOperationException_When_NamedStepIsLocked()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(
            requestId, "target-step", StepExecutionStatus.Failed,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(10));
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryStepAsync(requestId, "target-step", CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync resets step, saves, and enqueues when step is Failed")]
    public async Task Should_ResetSaveAndEnqueue_When_NamedStepIsFailed()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(requestId, "target-step", StepExecutionStatus.Failed);
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await handler.RetryStepAsync(requestId, "target-step", CancellationToken.None);

        Assert.Equal(StepExecutionStatus.Running, step.ExecutionStatus);
        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync resets step, saves, and enqueues when step is RetryScheduled")]
    public async Task Should_ResetSaveAndEnqueue_When_NamedStepIsRetryScheduled()
    {
        var requestId = Guid.NewGuid();
        var step = BuildRetryableStep(requestId, "target-step", StepExecutionStatus.RetryScheduled);
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await handler.RetryStepAsync(requestId, "target-step", CancellationToken.None);

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
            stepName: "target-step",
            executionStatus: StepExecutionStatus.Failed,
            messageType: "Bad.Type, BadAssembly");
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId, status: RequestStatus.InProgress, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RetryStepAsync(requestId, "target-step", CancellationToken.None));
    }
}
