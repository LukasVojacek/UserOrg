using System.Text.Json;
using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Domain;

public sealed class RequestAggregateTests
{
    private static readonly DateTimeOffset Now = new(2024, 3, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Create produces Accepted aggregate with a new RequestId")]
    public void Should_ProduceAcceptedAggregate_When_CreateCalled()
    {
        var payload = JsonDocument.Parse("""{"project":"p1"}""");

        var aggregate = RequestAggregate.Create("my-strategy", payload, "client-1", "https://callback", "key-1", Now);

        Assert.NotEqual(Guid.Empty, aggregate.RequestId);
        Assert.Equal("my-strategy", aggregate.Strategy);
        Assert.Same(payload, aggregate.Payload);
        Assert.Equal("client-1", aggregate.ClientId);
        Assert.Equal("https://callback", aggregate.CallbackUrl);
        Assert.Equal("key-1", aggregate.IdempotencyKey);
        Assert.Equal(RequestStatus.Accepted, aggregate.Status);
        Assert.Equal(Now, aggregate.CreatedAt);
        Assert.Equal(Now, aggregate.UpdatedAt);
        Assert.Empty(aggregate.Steps);
        Assert.Null(aggregate.CurrentState);
        Assert.Null(aggregate.ErrorCode);
        Assert.Null(aggregate.CompletedAt);
    }

    [Fact(DisplayName = "Create throws ArgumentException when strategy is empty")]
    public void Should_ThrowArgumentException_When_CreateCalledWithEmptyStrategy()
    {
        Assert.Throws<ArgumentException>(
            () => RequestAggregate.Create(string.Empty, JsonDocument.Parse("{}"), null, null, null, Now));
    }

    [Fact(DisplayName = "Create throws ArgumentNullException when payload is null")]
    public void Should_ThrowArgumentNullException_When_CreateCalledWithNullPayload()
    {
        Assert.Throws<ArgumentNullException>(
            () => RequestAggregate.Create("strategy", null!, null, null, null, Now));
    }

    [Fact(DisplayName = "Create generates unique RequestId each call")]
    public void Should_GenerateUniqueRequestId_When_CreateCalledTwice()
    {
        var a = RequestAggregate.Create("s", JsonDocument.Parse("{}"), null, null, null, Now);
        var b = RequestAggregate.Create("s", JsonDocument.Parse("{}"), null, null, null, Now);

        Assert.NotEqual(a.RequestId, b.RequestId);
    }

    [Fact(DisplayName = "Rehydrate restores all scalar fields exactly")]
    public void Should_RestoreAllScalarFields_When_RehydrateCalled()
    {
        var id = Guid.NewGuid();
        var payload = JsonDocument.Parse("{}");
        var result = JsonDocument.Parse("""{"r":1}""");
        var completed = Now.AddHours(1);

        var aggregate = RequestAggregate.Rehydrate(
            requestId: id,
            strategy: "strat",
            payload: payload,
            status: RequestStatus.Completed,
            currentState: "Done",
            clientId: "c1",
            callbackUrl: "https://cb",
            idempotencyKey: "ik",
            result: result,
            errorCode: null,
            errorMessage: null,
            createdAt: Now,
            updatedAt: Now.AddMinutes(5),
            completedAt: completed,
            rowVersion: [1, 2, 3],
            steps: []);

        Assert.Equal(id, aggregate.RequestId);
        Assert.Equal("strat", aggregate.Strategy);
        Assert.Same(payload, aggregate.Payload);
        Assert.Equal(RequestStatus.Completed, aggregate.Status);
        Assert.Equal("Done", aggregate.CurrentState);
        Assert.Equal("c1", aggregate.ClientId);
        Assert.Equal("https://cb", aggregate.CallbackUrl);
        Assert.Equal("ik", aggregate.IdempotencyKey);
        Assert.Same(result, aggregate.Result);
        Assert.Equal(Now, aggregate.CreatedAt);
        Assert.Equal(Now.AddMinutes(5), aggregate.UpdatedAt);
        Assert.Equal(completed, aggregate.CompletedAt);
        Assert.Equal([1, 2, 3], aggregate.RowVersion);
        Assert.Empty(aggregate.Steps);
    }

    [Fact(DisplayName = "Rehydrate restores steps collection")]
    public void Should_RestoreSteps_When_RehydrateCalledWithSteps()
    {
        var requestId = Guid.NewGuid();
        var step1 = RequestAggregateBuilder.BuildStep(requestId: requestId, stepName: "Step1");
        var step2 = RequestAggregateBuilder.BuildStep(requestId: requestId, stepName: "Step2");

        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, steps: [step1, step2]);

        Assert.Equal(2, aggregate.Steps.Count);
        Assert.Equal("Step1", aggregate.Steps[0].StepName);
        Assert.Equal("Step2", aggregate.Steps[1].StepName);
    }

    [Fact(DisplayName = "RecordStepStarted transitions aggregate to InProgress and adds step")]
    public void Should_TransitionToInProgressAndAddStep_When_RecordStepStartedCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.Accepted);
        var inputData = JsonDocument.Parse("{}");

        aggregate.RecordStepStarted("Step1", inputData, "MyType, MyAssembly", Now);

        Assert.Equal(RequestStatus.InProgress, aggregate.Status);
        Assert.Equal("Step1", aggregate.CurrentState);
        Assert.Equal(Now, aggregate.UpdatedAt);
        Assert.Single(aggregate.Steps);
        Assert.Equal("Step1", aggregate.Steps[0].StepName);
        Assert.Equal(StepExecutionStatus.Running, aggregate.Steps[0].ExecutionStatus);
    }

    [Fact(DisplayName = "RecordStepCompleted marks the step Completed and stores result")]
    public void Should_MarkStepCompletedWithResult_When_RecordStepCompletedCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        var resultData = JsonDocument.Parse("""{"done":true}""");

        aggregate.RecordStepCompleted("Step1", resultData, Now.AddSeconds(5));

        var step = aggregate.Steps[0];
        Assert.Equal(StepExecutionStatus.Completed, step.ExecutionStatus);
        Assert.Same(resultData, step.Result);
        Assert.Equal(Now.AddSeconds(5), step.CompletedAt);
    }

    [Fact(DisplayName = "RecordStepCompleted throws when step does not exist")]
    public void Should_Throw_When_RecordStepCompletedCalledForMissingStep()
    {
        var aggregate = RequestAggregateBuilder.Build();

        Assert.Throws<InvalidOperationException>(
            () => aggregate.RecordStepCompleted("NonExistentStep", null, Now));
    }

    [Fact(DisplayName = "RecordStepFailed marks step Failed and stores error info")]
    public void Should_MarkStepFailedWithErrorInfo_When_RecordStepFailedCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        var error = new ErrorInfo("API_ERR", "API returned 500");

        aggregate.RecordStepFailed("Step1", error, Now.AddSeconds(2));

        var step = aggregate.Steps[0];
        Assert.Equal(StepExecutionStatus.Failed, step.ExecutionStatus);
        Assert.Equal("API_ERR", step.ErrorCode);
        Assert.Equal("API returned 500", step.ErrorMessage);
        Assert.Equal(Now.AddSeconds(2), step.CompletedAt);
    }

    [Fact(DisplayName = "RecordStepRetryScheduled increments retries and sets NextRetryAt")]
    public void Should_IncrementRetriesAndSetNextRetryAt_When_RecordStepRetryScheduledCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        var nextRetry = Now.AddMinutes(5);

        aggregate.RecordStepRetryScheduled("Step1", new ErrorInfo("TIMEOUT", "timed out"), nextRetry, Now);

        var step = aggregate.Steps[0];
        Assert.Equal(StepExecutionStatus.RetryScheduled, step.ExecutionStatus);
        Assert.Equal(1, step.StepRetries);
        Assert.Equal(nextRetry, step.NextRetryAt);
        Assert.Equal("TIMEOUT", step.ErrorCode);
        Assert.Equal("timed out", step.ErrorMessage);
    }

    [Fact(DisplayName = "RecordStepRetryScheduled increments retries cumulatively on each call")]
    public void Should_IncrementRetriesCumulatively_When_RecordStepRetryScheduledCalledMultipleTimes()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);

        aggregate.RecordStepRetryScheduled("Step1", new ErrorInfo("E", "m"), Now.AddMinutes(1), Now);
        aggregate.RecordStepRetryScheduled("Step1", new ErrorInfo("E", "m"), Now.AddMinutes(2), Now);

        Assert.Equal(2, aggregate.Steps[0].StepRetries);
    }

    [Fact(DisplayName = "SetCurrentState updates CurrentState and UpdatedAt")]
    public void Should_UpdateCurrentStateAndUpdatedAt_When_SetCurrentStateCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(currentState: "Step1");

        aggregate.SetCurrentState("Step2", Now.AddSeconds(10));

        Assert.Equal("Step2", aggregate.CurrentState);
        Assert.Equal(Now.AddSeconds(10), aggregate.UpdatedAt);
    }

    [Fact(DisplayName = "MarkAwaitingSignal parks the request at the gate")]
    public void Should_ParkAtGate_When_MarkAwaitingSignalCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress, currentState: "Step1");

        aggregate.MarkAwaitingSignal("ApprovalGate", Now.AddSeconds(5));

        Assert.Equal(RequestStatus.AwaitingSignal, aggregate.Status);
        Assert.Equal("ApprovalGate", aggregate.CurrentState);
        Assert.Equal(Now.AddSeconds(5), aggregate.UpdatedAt);
    }

    [Fact(DisplayName = "MarkCompleted sets Completed status and CompletedAt")]
    public void Should_SetCompletedStatusAndCompletedAt_When_MarkCompletedCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        var result = JsonDocument.Parse("""{"summary":"done"}""");

        aggregate.MarkCompleted(result, Now);

        Assert.Equal(RequestStatus.Completed, aggregate.Status);
        Assert.Same(result, aggregate.Result);
        Assert.Equal(Now, aggregate.CompletedAt);
        Assert.Equal(Now, aggregate.UpdatedAt);
    }

    [Fact(DisplayName = "MarkCompleted accepts null result")]
    public void Should_AcceptNullResult_When_MarkCompletedCalledWithNull()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);

        aggregate.MarkCompleted(null, Now);

        Assert.Equal(RequestStatus.Completed, aggregate.Status);
        Assert.Null(aggregate.Result);
    }

    [Fact(DisplayName = "MarkFailed sets Failed status, error fields, and CompletedAt")]
    public void Should_SetFailedStatusAndErrorFields_When_MarkFailedCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        var error = new ErrorInfo("PERMANENT_FAILURE", "step blew up");

        aggregate.MarkFailed(error, Now);

        Assert.Equal(RequestStatus.Failed, aggregate.Status);
        Assert.Equal("PERMANENT_FAILURE", aggregate.ErrorCode);
        Assert.Equal("step blew up", aggregate.ErrorMessage);
        Assert.Equal(Now, aggregate.CompletedAt);
        Assert.Equal(Now, aggregate.UpdatedAt);
    }

    [Fact(DisplayName = "ResetStepForRetry clears error fields and resets step to Running")]
    public void Should_ClearErrorFieldsAndResetToRunning_When_ResetStepForRetryCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        aggregate.RecordStepFailed("Step1", new ErrorInfo("ERR", "fail"), Now.AddSeconds(1));

        aggregate.ResetStepForRetry("Step1", Now.AddSeconds(2));

        var step = aggregate.Steps[0];
        Assert.Equal(StepExecutionStatus.Running, step.ExecutionStatus);
        Assert.Null(step.ErrorCode);
        Assert.Null(step.ErrorMessage);
        Assert.Null(step.NextRetryAt);
        Assert.Null(step.LockedBy);
        Assert.Null(step.LockedUntil);
        Assert.Null(step.CompletedAt);
    }

    [Fact(DisplayName = "ResetStepForRetry transitions aggregate from Failed to InProgress")]
    public void Should_TransitionAggregateFromFailedToInProgress_When_ResetStepForRetryCalled()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        aggregate.RecordStepFailed("Step1", new ErrorInfo("ERR", "error"), Now.AddSeconds(1));
        aggregate.MarkFailed(new ErrorInfo("ERR", "error"), Now.AddSeconds(2));

        aggregate.ResetStepForRetry("Step1", Now.AddSeconds(3));

        Assert.Equal(RequestStatus.InProgress, aggregate.Status);
        Assert.Null(aggregate.ErrorCode);
        Assert.Null(aggregate.ErrorMessage);
        Assert.Null(aggregate.CompletedAt);
    }

    [Fact(DisplayName = "ResetStepForRetry does not change aggregate status when already InProgress")]
    public void Should_NotChangeAggregateStatus_When_ResetStepForRetryCalledOnInProgressAggregate()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        aggregate.RecordStepFailed("Step1", new ErrorInfo("E", "m"), Now.AddSeconds(1));

        aggregate.ResetStepForRetry("Step1", Now.AddSeconds(2));

        Assert.Equal(RequestStatus.InProgress, aggregate.Status);
    }

    [Fact(DisplayName = "ResetStepForRetry throws when step does not exist")]
    public void Should_Throw_When_ResetStepForRetryCalledForMissingStep()
    {
        var aggregate = RequestAggregateBuilder.Build();

        Assert.Throws<InvalidOperationException>(
            () => aggregate.ResetStepForRetry("NoSuchStep", Now));
    }

    [Fact(DisplayName = "IsStepCompleted returns false when step does not exist")]
    public void Should_ReturnFalse_When_IsStepCompletedCalledForMissingStep()
    {
        var aggregate = RequestAggregateBuilder.Build();

        Assert.False(aggregate.IsStepCompleted("NoSuchStep"));
    }

    [Fact(DisplayName = "IsStepCompleted returns false when step is Running")]
    public void Should_ReturnFalse_When_StepIsRunning()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);

        Assert.False(aggregate.IsStepCompleted("Step1"));
    }

    [Fact(DisplayName = "IsStepCompleted returns true after RecordStepCompleted")]
    public void Should_ReturnTrue_When_StepHasBeenCompleted()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        aggregate.RecordStepCompleted("Step1", null, Now.AddSeconds(1));

        Assert.True(aggregate.IsStepCompleted("Step1"));
    }

    [Fact(DisplayName = "IsStepCompleted returns false for a Failed step")]
    public void Should_ReturnFalse_When_StepIsFailed()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        aggregate.RecordStepFailed("Step1", new ErrorInfo("E", "m"), Now.AddSeconds(1));

        Assert.False(aggregate.IsStepCompleted("Step1"));
    }

    [Fact(DisplayName = "GetStepResult returns null when step does not exist")]
    public void Should_ReturnNull_When_GetStepResultCalledForMissingStep()
    {
        var aggregate = RequestAggregateBuilder.Build();

        Assert.Null(aggregate.GetStepResult("NoSuchStep"));
    }

    [Fact(DisplayName = "GetStepResult returns null when step is not completed")]
    public void Should_ReturnNull_When_StepIsNotCompleted()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);

        Assert.Null(aggregate.GetStepResult("Step1"));
    }

    [Fact(DisplayName = "GetStepResult returns the stored result after completion")]
    public void Should_ReturnStoredResult_When_StepHasCompleted()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        var resultData = JsonDocument.Parse("""{"val":42}""");
        aggregate.RecordStepCompleted("Step1", resultData, Now.AddSeconds(1));

        var actual = aggregate.GetStepResult("Step1");

        Assert.Same(resultData, actual);
    }

    private sealed record SampleResult(int Val, string Name);

    [Fact(DisplayName = "ResultOf deserializes the named step's result to the requested type")]
    public void Should_DeserializeStoredResult_When_ResultOfCalledForCompletedStep()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        aggregate.RecordStepCompleted("Step1", JsonDocument.Parse("""{"Val":42,"Name":"answer"}"""), Now.AddSeconds(1));

        var actual = aggregate.ResultOf<SampleResult>("Step1");

        Assert.Equal(new SampleResult(42, "answer"), actual);
    }

    [Fact(DisplayName = "ResultOf throws when the named step has no completed result")]
    public void Should_Throw_When_ResultOfCalledForMissingStep()
    {
        var aggregate = RequestAggregateBuilder.Build();

        Assert.Throws<InvalidOperationException>(() => aggregate.ResultOf<SampleResult>("NoSuchStep"));
    }

    [Fact(DisplayName = "ResultOf throws when the named step completed without data")]
    public void Should_Throw_When_ResultOfCalledForStepWithNullResult()
    {
        var aggregate = RequestAggregateBuilder.Build(status: RequestStatus.InProgress);
        aggregate.RecordStepStarted("Step1", JsonDocument.Parse("{}"), "T, A", Now);
        aggregate.RecordStepCompleted("Step1", null, Now.AddSeconds(1));

        Assert.Throws<InvalidOperationException>(() => aggregate.ResultOf<SampleResult>("Step1"));
    }

    [Fact(DisplayName = "GetLastStep returns null when no steps exist")]
    public void Should_ReturnNull_When_NoStepsExist()
    {
        var aggregate = RequestAggregateBuilder.Build();

        Assert.Null(aggregate.GetLastStep());
    }

    [Fact(DisplayName = "GetLastStep returns the step with the latest StartedAt")]
    public void Should_ReturnMostRecentStep_When_MultipleStepsExist()
    {
        var requestId = Guid.NewGuid();
        var earlier = RequestAggregateBuilder.BuildStep(
            requestId: requestId, stepName: "Step1",
            startedAt: Now.AddMinutes(-5));
        var later = RequestAggregateBuilder.BuildStep(
            requestId: requestId, stepName: "Step2",
            startedAt: Now);

        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, steps: [earlier, later]);

        var last = aggregate.GetLastStep();
        Assert.NotNull(last);
        Assert.Equal("Step2", last.StepName);
    }

    [Fact(DisplayName = "GetLastStep returns the only step when exactly one step exists")]
    public void Should_ReturnOnlyStep_When_ExactlyOneStepExists()
    {
        var requestId = Guid.NewGuid();
        var step = RequestAggregateBuilder.BuildStep(requestId: requestId, stepName: "OnlyStep");
        var aggregate = RequestAggregateBuilder.Build(requestId: requestId, steps: [step]);

        var last = aggregate.GetLastStep();

        Assert.NotNull(last);
        Assert.Equal("OnlyStep", last.StepName);
    }
}
