using System.Text.Json;
using NSubstitute;
using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Application;
using WorkflowEngine.Core.Features.Retry;
using WorkflowEngine.Core.Features.Submit;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace WorkflowEngine.Core.Tests.Features;

public sealed class GetStatusHandlerTests
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

    [Fact(DisplayName = "GetStatusAsync returns null when request is not found")]
    public async Task Should_ReturnNull_When_RequestNotFound()
    {
        var requestId = Guid.NewGuid();
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>())
            .Returns((WorkflowEngine.Core.Domain.Request.RequestAggregate?)null);
        var service = CreateService();

        var result = await service.GetStatusAsync(requestId, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact(DisplayName = "GetStatusAsync maps aggregate fields to StatusResponse")]
    public async Task Should_MapAggregateFieldsToStatusResponse_When_RequestFound()
    {
        var requestId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            strategy: "my-strategy",
            status: RequestStatus.InProgress,
            currentState: "Step1",
            clientId: "client-99",
            callbackUrl: "https://cb",
            idempotencyKey: "ik",
            createdAt: now,
            updatedAt: now.AddMinutes(1));
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>())
            .Returns(aggregate);
        var service = CreateService();

        var result = await service.GetStatusAsync(requestId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(requestId, result.RequestId);
        Assert.Equal("my-strategy", result.Strategy);
        Assert.Equal(RequestStatus.InProgress, result.Status);
        Assert.Equal("Step1", result.CurrentState);
        Assert.Equal("client-99", result.ClientId);
        Assert.Equal(now, result.CreatedAt);
        Assert.Equal(now.AddMinutes(1), result.UpdatedAt);
        Assert.Empty(result.Steps);
    }

    [Fact(DisplayName = "GetStatusAsync maps step fields into StepStatusItems")]
    public async Task Should_MapStepFields_When_AggregateHasSteps()
    {
        var requestId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var completedAt = DateTimeOffset.UtcNow;
        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            stepName: "ProcessData",
            executionStatus: StepExecutionStatus.Completed,
            stepRetries: 2,
            startedAt: startedAt,
            completedAt: completedAt,
            errorCode: null,
            errorMessage: null);
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        var result = await service.GetStatusAsync(requestId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result.Steps);
        var stepItem = result.Steps[0];
        Assert.Equal("ProcessData", stepItem.StateName);
        Assert.Equal(StepExecutionStatus.Completed, stepItem.ExecutionStatus);
        Assert.Equal(2, stepItem.StepRetries);
        Assert.Equal(startedAt, stepItem.StartedAt);
        Assert.Equal(completedAt, stepItem.CompletedAt);
    }

    [Fact(DisplayName = "GetStatusAsync maps error fields from a failed step")]
    public async Task Should_MapStepErrorFields_When_StepIsFailed()
    {
        var requestId = Guid.NewGuid();
        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            stepName: "FailedStep",
            executionStatus: StepExecutionStatus.Failed,
            errorCode: "API_ERROR",
            errorMessage: "The API returned 500");
        var aggregate = RequestAggregateBuilder.Build(
            requestId: requestId,
            status: RequestStatus.Failed,
            steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        var result = await service.GetStatusAsync(requestId, CancellationToken.None);

        Assert.NotNull(result);
        var stepItem = result.Steps[0];
        Assert.Equal("API_ERROR", stepItem.ErrorCode);
        Assert.Equal("The API returned 500", stepItem.ErrorMessage);
    }

    [Fact(DisplayName = "GetStatusAsync maps NextRetryAt for a retry-scheduled step")]
    public async Task Should_MapNextRetryAt_When_StepIsRetryScheduled()
    {
        var requestId = Guid.NewGuid();
        var nextRetryAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var step = RequestAggregateBuilder.BuildStep(
            requestId: requestId,
            stepName: "RetryStep",
            executionStatus: StepExecutionStatus.RetryScheduled,
            stepRetries: 1,
            nextRetryAt: nextRetryAt);
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, steps: [step]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        var result = await service.GetStatusAsync(requestId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(nextRetryAt, result.Steps[0].NextRetryAt);
    }

    [Fact(DisplayName = "GetStatusAsync maps multiple steps preserving order")]
    public async Task Should_MapAllStepsInOrder_When_AggregateHasMultipleSteps()
    {
        var requestId = Guid.NewGuid();
        var step1 = RequestAggregateBuilder.BuildStep(requestId: requestId, stepName: "Step1");
        var step2 = RequestAggregateBuilder.BuildStep(requestId: requestId, stepName: "Step2");
        var step3 = RequestAggregateBuilder.BuildStep(requestId: requestId, stepName: "Step3");
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, steps: [step1, step2, step3]);
        _repository.FindByIdAsync(requestId, Arg.Any<CancellationToken>()).Returns(aggregate);
        var service = CreateService();

        var result = await service.GetStatusAsync(requestId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal("Step1", result.Steps[0].StateName);
        Assert.Equal("Step2", result.Steps[1].StateName);
        Assert.Equal("Step3", result.Steps[2].StateName);
    }
}
