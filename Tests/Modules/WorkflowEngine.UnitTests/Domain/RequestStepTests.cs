using System.Text.Json;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Tests.Domain;

public sealed class RequestStepTests
{

    [Fact(DisplayName = "Rehydrate restores all provided field values")]
    public void Should_RestoreAllFields_When_RehydrateCalled()
    {
        var stepId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var inputData = JsonDocument.Parse("""{"key":"value"}""");
        var result = JsonDocument.Parse("""{"result":true}""");
        var startedAt = new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var completedAt = new DateTimeOffset(2024, 1, 1, 10, 5, 0, TimeSpan.Zero);
        var nextRetryAt = new DateTimeOffset(2024, 1, 1, 10, 10, 0, TimeSpan.Zero);

        var step = RequestStep.Rehydrate(
            stepId: stepId,
            requestId: requestId,
            stepName: "CallApi",
            executionStatus: StepExecutionStatus.Failed,
            messageType: "MyApp.StepRequest, MyApp",
            inputData: inputData,
            stepRetries: 3,
            nextRetryAt: nextRetryAt,
            lockedBy: "worker-1",
            lockedUntil: completedAt,
            startedAt: startedAt,
            completedAt: completedAt,
            result: result,
            errorCode: "API_ERROR",
            errorMessage: "API call failed");

        Assert.Equal(stepId, step.StepId);
        Assert.Equal(requestId, step.RequestId);
        Assert.Equal("CallApi", step.StepName);
        Assert.Equal(StepExecutionStatus.Failed, step.ExecutionStatus);
        Assert.Equal("MyApp.StepRequest, MyApp", step.MessageType);
        Assert.Same(inputData, step.InputData);
        Assert.Equal(3, step.StepRetries);
        Assert.Equal(nextRetryAt, step.NextRetryAt);
        Assert.Equal("worker-1", step.LockedBy);
        Assert.Equal(completedAt, step.LockedUntil);
        Assert.Equal(startedAt, step.StartedAt);
        Assert.Equal(completedAt, step.CompletedAt);
        Assert.Same(result, step.Result);
        Assert.Equal("API_ERROR", step.ErrorCode);
        Assert.Equal("API call failed", step.ErrorMessage);
    }

    [Fact(DisplayName = "Rehydrate accepts null optional fields")]
    public void Should_AcceptNulls_When_OptionalFieldsOmitted()
    {
        var step = RequestStep.Rehydrate(
            stepId: Guid.NewGuid(),
            requestId: Guid.NewGuid(),
            stepName: "Init",
            executionStatus: StepExecutionStatus.Running,
            messageType: "T, A",
            inputData: null,
            stepRetries: 0,
            nextRetryAt: null,
            lockedBy: null,
            lockedUntil: null,
            startedAt: null,
            completedAt: null,
            result: null,
            errorCode: null,
            errorMessage: null);

        Assert.Null(step.InputData);
        Assert.Null(step.NextRetryAt);
        Assert.Null(step.LockedBy);
        Assert.Null(step.LockedUntil);
        Assert.Null(step.StartedAt);
        Assert.Null(step.CompletedAt);
        Assert.Null(step.Result);
        Assert.Null(step.ErrorCode);
        Assert.Null(step.ErrorMessage);
    }

    [Fact(DisplayName = "StartNew creates a running step with the given fields")]
    public void Should_CreateRunningStep_When_StartNewCalled()
    {
        var requestId = Guid.NewGuid();
        var inputData = JsonDocument.Parse("{}");
        var startedAt = new DateTimeOffset(2024, 6, 1, 9, 0, 0, TimeSpan.Zero);

        var step = RequestStep.StartNew(requestId, "ProcessData", inputData, "MyType, MyAssembly", startedAt);

        Assert.NotEqual(Guid.Empty, step.StepId);
        Assert.Equal(requestId, step.RequestId);
        Assert.Equal("ProcessData", step.StepName);
        Assert.Equal(StepExecutionStatus.Running, step.ExecutionStatus);
        Assert.Equal("MyType, MyAssembly", step.MessageType);
        Assert.Same(inputData, step.InputData);
        Assert.Equal(0, step.StepRetries);
        Assert.Equal(startedAt, step.StartedAt);
        Assert.Null(step.CompletedAt);
        Assert.Null(step.ErrorCode);
        Assert.Null(step.LockedBy);
    }

    [Fact(DisplayName = "StartNew generates a unique StepId each call")]
    public void Should_GenerateUniqueStepId_When_StartNewCalledTwice()
    {
        var inputData = JsonDocument.Parse("{}");
        var now = DateTimeOffset.UtcNow;

        var step1 = RequestStep.StartNew(Guid.NewGuid(), "A", inputData, "T, A", now);
        var step2 = RequestStep.StartNew(Guid.NewGuid(), "A", inputData, "T, A", now);

        Assert.NotEqual(step1.StepId, step2.StepId);
    }
}
