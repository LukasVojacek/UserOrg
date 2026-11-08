using System.Text.Json;

using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Tests.Domain;

/// <summary>
/// How many <see cref="RequestStep"/> rows a step name owns, and which of them carries the attempt count.
///
/// <para>
/// T-121 D1: a retry reuses the live row of its step name and carries <c>StepRetries</c> forward, so a step
/// that is mid-budget has exactly one row and that row is the attempt history the worker's budget guard and
/// back-off read. D2: a <c>Completed</c> row is never reused — a step name that legitimately runs again after
/// completing appends, because reusing the completed row would erase the result <c>GetStepResult</c> and
/// <c>IsStepCompleted</c> read.
/// </para>
/// </summary>
public sealed class StepRowLifecycleTests
{
    private const string StepName = "CallHEAppE";

    private static readonly Guid RequestId = Guid.Parse("2f4a9c1e-0000-4000-8000-000000000121");

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly string MessageType = typeof(StepRowLifecycleTests).AssemblyQualifiedName!;

    [Fact(DisplayName = "A step that starts again while its row is RetryScheduled reuses that row and keeps its retry count")]
    public void Should_ReuseTheLiveRow_When_ARetryScheduledStepStartsAgain()
    {
        var aggregate = NewRequest();

        aggregate.RecordStepStarted(StepName, Input("first"), MessageType, Now);
        aggregate.RecordStepRetryScheduled(
            StepName,
            new ErrorInfo("TRANSIENT", "HEAppE timed out"),
            Now.AddSeconds(10),
            Now.AddSeconds(1));

        aggregate.RecordStepStarted(StepName, Input("second"), MessageType, Now.AddSeconds(10));

        var step = Assert.Single(aggregate.Steps);
        Assert.Equal(StepExecutionStatus.Running, step.ExecutionStatus);
        Assert.Equal(1, step.StepRetries);
        Assert.Null(step.NextRetryAt);
        Assert.Null(step.ErrorCode);
        Assert.Null(step.ErrorMessage);
        Assert.Equal(Now.AddSeconds(10), step.StartedAt);
        Assert.Equal("second", step.InputData!.RootElement.GetProperty("attempt").GetString());
    }

    [Fact(DisplayName = "A step name that completes and then starts again gets a second row, and the completed row keeps its result")]
    public void Should_AppendASecondRow_When_ACompletedStepNameStartsAgain()
    {
        var aggregate = NewRequest();

        aggregate.RecordStepStarted(StepName, Input("first"), MessageType, Now);
        aggregate.RecordStepCompleted(StepName, JsonDocument.Parse("""{"jobId":42}"""), Now.AddSeconds(1));

        aggregate.RecordStepStarted(StepName, Input("second"), MessageType, Now.AddSeconds(2));

        Assert.Equal(2, aggregate.Steps.Count(s => s.StepName == StepName));

        var completed = Assert.Single(aggregate.Steps, s => s.ExecutionStatus == StepExecutionStatus.Completed);
        Assert.Equal(Now.AddSeconds(1), completed.CompletedAt);

        Assert.True(aggregate.IsStepCompleted(StepName));
        Assert.Equal(42, aggregate.GetStepResult(StepName)!.RootElement.GetProperty("jobId").GetInt32());

        var live = Assert.Single(aggregate.Steps, s => s.ExecutionStatus == StepExecutionStatus.Running);
        Assert.Equal(0, live.StepRetries);
        Assert.Equal(Now.AddSeconds(2), live.StartedAt);
    }

    [Fact(DisplayName = "The retry counter lands on the live row when the loaded order puts the Completed row of the same step name last")]
    public void Should_CountTheRetryOnTheLiveRow_When_ACompletedRowIsLoadedAfterIt()
    {
        var live = Row(StepExecutionStatus.Running, startedAt: Now.AddSeconds(2));
        var completed = Row(StepExecutionStatus.Completed, startedAt: Now, completedAt: Now.AddSeconds(1));

        // why: Include(r => r.Steps) declares no OrderBy, so the loaded order is the database's choice. This
        // is the order that puts the Completed row last — the row RequireStep's FindLast then resolves to.
        var aggregate = RehydrateWith(live, completed);

        aggregate.RecordStepRetryScheduled(
            StepName,
            new ErrorInfo("TRANSIENT", "HEAppE timed out"),
            Now.AddSeconds(20),
            Now.AddSeconds(3));

        Assert.True(
            live.StepRetries == 1,
            $"The attempt was counted on the wrong row: the live row is still at StepRetries = {live.StepRetries} while the Completed row moved to {completed.StepRetries}. The budget guard reads the live row, so an attempt recorded anywhere else is an attempt the budget never sees.");

        Assert.True(
            completed.StepRetries == 0 && completed.ExecutionStatus == StepExecutionStatus.Completed,
            $"The Completed row was rewritten by the retry: StepRetries = {completed.StepRetries}, ExecutionStatus = {completed.ExecutionStatus}. A completed attempt keeps its result and its status whatever else the step name does later.");
    }

    [Fact(DisplayName = "The retry counter lands on the live row when the loaded order puts the Completed row of the same step name first")]
    public void Should_CountTheRetryOnTheLiveRow_When_ACompletedRowIsLoadedBeforeIt()
    {
        var completed = Row(StepExecutionStatus.Completed, startedAt: Now, completedAt: Now.AddSeconds(1));
        var live = Row(StepExecutionStatus.Running, startedAt: Now.AddSeconds(2));

        var aggregate = RehydrateWith(completed, live);

        aggregate.RecordStepRetryScheduled(
            StepName,
            new ErrorInfo("TRANSIENT", "HEAppE timed out"),
            Now.AddSeconds(20),
            Now.AddSeconds(3));

        Assert.True(
            live.StepRetries == 1,
            $"The attempt was counted on the wrong row: the live row is still at StepRetries = {live.StepRetries} while the Completed row moved to {completed.StepRetries}.");

        Assert.True(
            completed.StepRetries == 0 && completed.ExecutionStatus == StepExecutionStatus.Completed,
            $"The Completed row was rewritten by the retry: StepRetries = {completed.StepRetries}, ExecutionStatus = {completed.ExecutionStatus}.");
    }

    /// <summary>
    /// A persisted row of <see cref="StepName"/>, built through the load path so the test owns the order the
    /// rows arrive in — the one thing <c>Include(r =&gt; r.Steps)</c> leaves to the database.
    /// </summary>
    private static RequestStep Row(
        StepExecutionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt = null)
        => RequestStep.Rehydrate(
            Guid.CreateVersion7(),
            RequestId,
            StepName,
            status,
            MessageType,
            Input("persisted"),
            stepRetries: 0,
            nextRetryAt: null,
            lockedBy: null,
            lockedUntil: null,
            startedAt: startedAt,
            completedAt: completedAt,
            result: null,
            errorCode: null,
            errorMessage: null);

    private static RequestAggregate RehydrateWith(params RequestStep[] steps)
        => RequestAggregate.Rehydrate(
            RequestId,
            "t121-strategy",
            JsonDocument.Parse("{}"),
            RequestStatus.InProgress,
            StepName,
            clientId: null,
            callbackUrl: null,
            idempotencyKey: null,
            result: null,
            errorCode: null,
            errorMessage: null,
            createdAt: Now,
            updatedAt: Now,
            completedAt: null,
            rowVersion: null,
            steps: steps);

    private static RequestAggregate NewRequest()
        => RequestAggregate.Create("t121-strategy", JsonDocument.Parse("{}"), null, null, null, Now);

    private static JsonDocument Input(string attempt)
        => JsonDocument.Parse($$"""{"attempt":"{{attempt}}"}""");
}
