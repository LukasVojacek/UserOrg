using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Features.Submit;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace WorkflowEngine.Core.Tests.Features;

public sealed class SubmitHandlerTests
{
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();
    private readonly IQueuePort _queue = Substitute.For<IQueuePort>();
    private readonly IStrategyRegistry _registry = Substitute.For<IStrategyRegistry>();
    private readonly IRequestStrategy _strategy = Substitute.For<IRequestStrategy>();

    private static ILogger<SubmitHandler> CreateNullLogger() => new NullLogger<SubmitHandler>();

    private SubmitHandler CreateHandler() => new(_repository, _queue, _registry, CreateNullLogger());

    private TestStepRequest MakeStep(
        Guid? requestId = null
        ) =>
        new(requestId ?? Guid.NewGuid(), "init");

    public SubmitHandlerTests()
    {
        _strategy.Name.Returns("test-strategy");
        _strategy.InitialStepType.Returns(typeof(TestStepRequest));
        _registry.ResolveByStepType(typeof(TestStepRequest)).Returns(_strategy);
    }

    [Fact(DisplayName = "Constructor throws ArgumentNullException when repository is null")]
    public void Should_ThrowArgumentNullException_When_RepositoryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SubmitHandler(null!, _queue, _registry, CreateNullLogger()));
    }

    [Fact(DisplayName = "Constructor throws ArgumentNullException when queue is null")]
    public void Should_ThrowArgumentNullException_When_QueueIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SubmitHandler(_repository, null!, _registry, CreateNullLogger()));
    }

    [Fact(DisplayName = "Constructor throws ArgumentNullException when registry is null")]
    public void Should_ThrowArgumentNullException_When_RegistryIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new SubmitHandler(_repository, _queue, null!, CreateNullLogger()));
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Fact(DisplayName = "HandleAsync throws ArgumentNullException when step is null")]
    public async Task Should_ThrowArgumentNullException_When_StepIsNull()
    {
        var handler = CreateHandler();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => handler.HandleAsync(null!, clientId: null, callbackUrl: null, idempotencyKey: null, ct: CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync creates aggregate, enqueues step, and returns Accepted response")]
    public async Task Should_CreateAggregateAndEnqueueAndReturnAccepted_When_NewRequestSubmitted()
    {
        var step = MakeStep();
        var handler = CreateHandler();

        var response = await handler.HandleAsync(step, clientId: "c1", callbackUrl: null, idempotencyKey: null, ct: CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.RequestId);
        Assert.Equal(step.RequestId, response.RequestId);
        Assert.Equal(RequestStatus.Accepted, response.Status);
        await _repository.Received(1).AddAsync(
            Arg.Is<RequestAggregate>(a =>
                a.RequestId == step.RequestId &&
                a.Strategy == "test-strategy" &&
                a.Status == RequestStatus.Accepted &&
                a.ClientId == "c1"),
            Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(step, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync uses step.RequestId as aggregate ID")]
    public async Task Should_UseStepRequestId_AsAggregateId()
    {
        var id = Guid.Parse("aaaabbbb-0000-0000-0000-000000000001");
        var step = MakeStep(requestId: id);
        var handler = CreateHandler();

        var response = await handler.HandleAsync(step, null, null, null, CancellationToken.None);

        Assert.Equal(id, response.RequestId);
    }

    [Fact(DisplayName = "HandleAsync returns idempotent response when idempotency key already exists")]
    public async Task Should_ReturnCachedResponse_When_IdempotencyKeyAlreadyExists()
    {
        var existingId = Guid.NewGuid();
        var existing = RequestAggregateBuilder.Build(
            requestId: existingId,
            status: RequestStatus.InProgress,
            idempotencyKey: "idem-key-1");
        _repository.FindByIdempotencyKeyAsync("idem-key-1", Arg.Any<CancellationToken>())
                   .Returns(existing);
        var handler = CreateHandler();
        var step = MakeStep();

        var response = await handler.HandleAsync(step, null, null, idempotencyKey: "idem-key-1", ct: CancellationToken.None);

        Assert.Equal(existingId, response.RequestId);
        Assert.Equal(RequestStatus.InProgress, response.Status);
        await _repository.DidNotReceive().AddAsync(Arg.Any<RequestAggregate>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync returns existing response when RequestId already exists")]
    public async Task Should_ReturnExistingResponse_When_RequestIdAlreadyExists()
    {
        var id = Guid.NewGuid();
        var existing = RequestAggregateBuilder.Build(requestId: id, status: RequestStatus.InProgress);
        _repository.FindByIdAsync(id, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = CreateHandler();
        var step = MakeStep(requestId: id);

        var response = await handler.HandleAsync(step, null, null, null, CancellationToken.None);

        Assert.Equal(id, response.RequestId);
        Assert.Equal(RequestStatus.InProgress, response.Status);
        await _repository.DidNotReceive().AddAsync(Arg.Any<RequestAggregate>(), Arg.Any<CancellationToken>());
        await _queue.DidNotReceive().EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync creates new request when idempotency key is not found")]
    public async Task Should_CreateNewRequest_When_IdempotencyKeyNotFound()
    {
        _repository.FindByIdempotencyKeyAsync("new-key", Arg.Any<CancellationToken>())
                   .Returns((RequestAggregate?)null);
        var handler = CreateHandler();
        var step = MakeStep();

        var response = await handler.HandleAsync(step, null, null, idempotencyKey: "new-key", ct: CancellationToken.None);

        Assert.Equal(RequestStatus.Accepted, response.Status);
        await _repository.Received(1).AddAsync(Arg.Any<RequestAggregate>(), Arg.Any<CancellationToken>());
        await _queue.Received(1).EnqueueAsync(Arg.Any<StepRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync propagates exception from strategy registry")]
    public async Task Should_PropagateException_When_RegistryThrows()
    {
        _registry.ResolveByStepType(Arg.Any<Type>()).Throws(new InvalidOperationException("not found"));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(MakeStep(), null, null, null, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync propagates exception from repository AddAsync")]
    public async Task Should_PropagateException_When_AddAsyncThrows()
    {
        _repository.AddAsync(Arg.Any<RequestAggregate>(), Arg.Any<CancellationToken>())
                   .Throws(new InvalidOperationException("db error"));
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(MakeStep(), null, null, null, CancellationToken.None));
    }

    [Fact(DisplayName = "HandleAsync skips idempotency check when key is null")]
    public async Task Should_SkipIdempotencyCheck_When_IdempotencyKeyIsNull()
    {
        var handler = CreateHandler();

        await handler.HandleAsync(MakeStep(), null, null, idempotencyKey: null, ct: CancellationToken.None);

        await _repository.DidNotReceive()
                         .FindByIdempotencyKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "HandleAsync skips idempotency check when key is whitespace")]
    public async Task Should_SkipIdempotencyCheck_When_IdempotencyKeyIsWhitespace()
    {
        var handler = CreateHandler();

        await handler.HandleAsync(MakeStep(), null, null, idempotencyKey: "   ", ct: CancellationToken.None);

        await _repository.DidNotReceive()
                         .FindByIdempotencyKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
