using NSubstitute;
using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Application;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Features.Retry;
using WorkflowEngine.Core.Features.Submit;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace WorkflowEngine.Core.Tests.Features;

public sealed class MarkRequestFailedHandlerTests
{
    private readonly IRequestRepository _repository = Substitute.For<IRequestRepository>();

    private static ILogger<T> CreateNullLogger<T>() => new NullLogger<T>();

    private RequestApplicationService CreateService()
    {
        var queue = Substitute.For<IQueuePort>();
        var registry = Substitute.For<IStrategyRegistry>();
        return new(
            new SubmitHandler(_repository, queue, registry, CreateNullLogger<SubmitHandler>()),
            new RetryHandler(_repository, queue, CreateNullLogger<RetryHandler>()),
            _repository,
            registry,
            queue,
            CreateNullLogger<RequestApplicationService>());
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Fact(DisplayName = "MarkRequestFailedAsync throws ArgumentNullException when errorCode is null")]
    public async Task Should_ThrowArgumentException_When_ErrorCodeIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.MarkRequestFailedAsync(Guid.NewGuid(), null!, "msg", CancellationToken.None));
    }

    [Fact(DisplayName = "MarkRequestFailedAsync throws ArgumentException when errorCode is whitespace")]
    public async Task Should_ThrowArgumentException_When_ErrorCodeIsWhitespace()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.MarkRequestFailedAsync(Guid.NewGuid(), "  ", "msg", CancellationToken.None));
    }

    [Fact(DisplayName = "MarkRequestFailedAsync throws ArgumentNullException when errorMessage is null")]
    public async Task Should_ThrowArgumentException_When_ErrorMessageIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.MarkRequestFailedAsync(Guid.NewGuid(), "CODE", null!, CancellationToken.None));
    }

    [Fact(DisplayName = "MarkRequestFailedAsync throws ArgumentException when errorMessage is whitespace")]
    public async Task Should_ThrowArgumentException_When_ErrorMessageIsWhitespace()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.MarkRequestFailedAsync(Guid.NewGuid(), "CODE", "   ", CancellationToken.None));
    }

    [Fact(DisplayName = "MarkRequestFailedAsync throws KeyNotFoundException when request is not found")]
    public async Task Should_ThrowKeyNotFoundException_When_RequestNotFound()
    {
        var requestId = Guid.NewGuid();
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>())
            .Returns((RequestAggregate?)null);
        var service = CreateService();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.MarkRequestFailedAsync(requestId, "CODE", "msg", CancellationToken.None));
    }

    [Fact(DisplayName = "MarkRequestFailedAsync throws InvalidOperationException when request is Completed")]
    public async Task Should_ThrowInvalidOperationException_When_RequestIsCompleted()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.Completed);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.MarkRequestFailedAsync(requestId, "CODE", "msg", CancellationToken.None));
    }

    [Fact(DisplayName = "MarkRequestFailedAsync marks InProgress request as Failed and saves")]
    public async Task Should_MarkFailedAndSave_When_RequestIsInProgress()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.InProgress);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        await service.MarkRequestFailedAsync(requestId, "ADMIN_FAIL", "manually closed", CancellationToken.None);

        Assert.Equal(RequestStatus.Failed, aggregate.Status);
        Assert.Equal("ADMIN_FAIL", aggregate.ErrorCode);
        Assert.Equal("manually closed", aggregate.ErrorMessage);
        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "MarkRequestFailedAsync marks Accepted request as Failed and saves")]
    public async Task Should_MarkFailedAndSave_When_RequestIsAccepted()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, status: RequestStatus.Accepted);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        await service.MarkRequestFailedAsync(requestId, "CLOSE", "admin close", CancellationToken.None);

        Assert.Equal(RequestStatus.Failed, aggregate.Status);
        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "MarkRequestFailedAsync re-fails an already Failed request and saves")]
    public async Task Should_MarkFailedAgainAndSave_When_RequestIsAlreadyFailed()
    {
        var requestId = Guid.NewGuid();
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            status: RequestStatus.Failed,
            errorCode: "OLD_ERR",
            errorMessage: "old error");
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        await service.MarkRequestFailedAsync(requestId, "NEW_ERR", "new error", CancellationToken.None);

        Assert.Equal("NEW_ERR", aggregate.ErrorCode);
        Assert.Equal("new error", aggregate.ErrorMessage);
        await _repository.Received(1).SaveAsync(aggregate, Arg.Any<CancellationToken>());
    }
}
