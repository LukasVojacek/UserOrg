using System.Text.Json;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Tests.Domain;

/// <summary>
/// Which row the aggregate acts on when a step name owns a live row and a <c>Completed</c> one, and the
/// loaded order puts the <c>Completed</c> row last.
///
/// <para>
/// T-121-04 C2: <c>Include(r =&gt; r.Steps)</c> declares no <c>OrderBy</c>, so the order the rows arrive in
/// is the database's choice. <c>RecordStepRetryScheduled</c> already reads the live row explicitly
/// (T-121-03); <c>RecordStepCompleted</c> and <c>ResetStepForRetry</c> still resolve through
/// <c>RequireStep</c>, a status-blind <c>FindLast</c>. Under the inverted order the first writes the result
/// onto a completed attempt and strands the running one, and the second re-opens a completed attempt,
/// leaving two live rows for one step name — the invariant ADR-0067's T-121 amendment now rests on.
/// </para>
///
/// <para>
/// The order is forced through <see cref="RequestAggregate.Rehydrate"/>, as in
/// <see cref="StepRowLifecycleTests"/>: no route through the aggregate's own API produces it, because
/// <c>RecordStepStarted</c> always appends and the <c>Completed</c> row is therefore created first. The
/// load path is the only place the order can invert, and the methods under repair are still the ones called.
/// </para>
/// </summary>
public sealed class StepRowLiveResolutionTests
{
    private const string StepName = "CallHEAppE";

    private const int EarlierResult = 41;
    private const int LiveResult = 99;

    private static readonly Guid RequestId = Guid.Parse("2f4a9c1e-0000-4000-8000-000000000121");

    private static readonly DateTimeOffset Now = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly string MessageType = typeof(StepRowLiveResolutionTests).AssemblyQualifiedName!;

    [Fact(DisplayName = "RecordStepCompleted completes the live row when the Completed row of the same step name is loaded after it")]
    public void Should_CompleteTheLiveRow_When_ACompletedRowIsLoadedAfterIt()
    {
        var live = Row(StepExecutionStatus.Running, startedAt: Now.AddSeconds(2));
        var earlier = CompletedRow();

        var aggregate = RehydrateWith(live, earlier);

        aggregate.RecordStepCompleted(StepName, Result(LiveResult), Now.AddSeconds(3));

        Assert.True(
            live.ExecutionStatus == StepExecutionStatus.Completed && JobIdOf(live) == LiveResult,
            $"RecordStepCompleted did not complete the running row: it is {live.ExecutionStatus} carrying {Describe(JobIdOf(live))}. The row the step is actually executing never leaves Running, so FindLiveStep keeps handing it to the retry budget guard and the request sits on a step that has already succeeded.");

        Assert.True(
            earlier.ExecutionStatus == StepExecutionStatus.Completed && JobIdOf(earlier) == EarlierResult,
            $"The earlier completed attempt was overwritten: the row loaded after the live one is {earlier.ExecutionStatus} carrying {Describe(JobIdOf(earlier))}, not the jobId {EarlierResult} it completed with. That result is what GetStepResult and IsStepCompleted read back.");

        Assert.Equal(Now.AddSeconds(1), earlier.CompletedAt);
    }

    [Fact(DisplayName = "ResetStepForRetry re-opens the live row when the Completed row of the same step name is loaded after it")]
    public void Should_ReopenTheLiveRow_When_ACompletedRowIsLoadedAfterIt()
    {
        var live = FailedRow();
        var earlier = CompletedRow();

        var aggregate = RehydrateWith(live, earlier);

        aggregate.ResetStepForRetry(StepName, Now.AddSeconds(4));

        Assert.True(
            live.ExecutionStatus == StepExecutionStatus.Running,
            $"ResetStepForRetry did not re-open the failed row: it is still {live.ExecutionStatus}. The retry was re-enqueued against a step whose own row never left its terminal status.");

        var liveRows = aggregate.Steps.Count(s => s.StepName == StepName
                                               && s.ExecutionStatus != StepExecutionStatus.Completed);

        Assert.True(
            liveRows == 1,
            $"The step name has {liveRows} rows that are not Completed after the retry was reset; the one-live-row-per (RequestId, StepName) invariant ADR-0067 rests on allows exactly 1. Re-opening the earlier Completed row is what adds the second: it is now {earlier.ExecutionStatus}.");

        Assert.True(
            aggregate.IsStepCompleted(StepName) && JobIdOf(earlier) == EarlierResult,
            $"The earlier completed attempt no longer reads as completed: its row is {earlier.ExecutionStatus} carrying {Describe(JobIdOf(earlier))}, so IsStepCompleted(\"{StepName}\") is {aggregate.IsStepCompleted(StepName)} and the jobId {EarlierResult} the flow already produced is no longer readable through GetStepResult.");
    }

    [Fact(DisplayName = "RecordStepCompleted completes the live row when the Completed row of the same step name is loaded before it")]
    public void Should_CompleteTheLiveRow_When_ACompletedRowIsLoadedBeforeIt()
    {
        var earlier = CompletedRow();
        var live = Row(StepExecutionStatus.Running, startedAt: Now.AddSeconds(2));

        var aggregate = RehydrateWith(earlier, live);

        aggregate.RecordStepCompleted(StepName, Result(LiveResult), Now.AddSeconds(3));

        Assert.True(
            live.ExecutionStatus == StepExecutionStatus.Completed && JobIdOf(live) == LiveResult,
            $"RecordStepCompleted did not complete the running row: it is {live.ExecutionStatus} carrying {Describe(JobIdOf(live))}.");

        Assert.True(
            earlier.ExecutionStatus == StepExecutionStatus.Completed && JobIdOf(earlier) == EarlierResult,
            $"The earlier completed attempt was overwritten: it is {earlier.ExecutionStatus} carrying {Describe(JobIdOf(earlier))}, not the jobId {EarlierResult} it completed with.");
    }

    [Fact(DisplayName = "ResetStepForRetry re-opens the live row when the Completed row of the same step name is loaded before it")]
    public void Should_ReopenTheLiveRow_When_ACompletedRowIsLoadedBeforeIt()
    {
        var earlier = CompletedRow();
        var live = FailedRow();

        var aggregate = RehydrateWith(earlier, live);

        aggregate.ResetStepForRetry(StepName, Now.AddSeconds(4));

        Assert.True(
            live.ExecutionStatus == StepExecutionStatus.Running,
            $"ResetStepForRetry did not re-open the failed row: it is still {live.ExecutionStatus}.");

        var liveRows = aggregate.Steps.Count(s => s.StepName == StepName
                                               && s.ExecutionStatus != StepExecutionStatus.Completed);

        Assert.True(
            liveRows == 1,
            $"The step name has {liveRows} rows that are not Completed after the retry was reset; the invariant allows exactly 1. The earlier Completed row is {earlier.ExecutionStatus}.");

        Assert.True(
            aggregate.IsStepCompleted(StepName) && JobIdOf(earlier) == EarlierResult,
            $"The earlier completed attempt no longer reads as completed: its row is {earlier.ExecutionStatus} carrying {Describe(JobIdOf(earlier))}.");
    }

    private static RequestStep CompletedRow()
        => Row(
            StepExecutionStatus.Completed,
            startedAt: Now,
            completedAt: Now.AddSeconds(1),
            result: Result(EarlierResult));

    private static RequestStep FailedRow()
        => Row(
            StepExecutionStatus.Failed,
            startedAt: Now.AddSeconds(2),
            completedAt: Now.AddSeconds(3),
            errorCode: "TRANSIENT");

    /// <summary>
    /// A persisted row of <see cref="StepName"/>, built through the load path so the test owns the order the
    /// rows arrive in — the one thing <c>Include(r =&gt; r.Steps)</c> leaves to the database.
    /// </summary>
    private static RequestStep Row(
        StepExecutionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt = null,
        JsonDocument? result = null,
        string? errorCode = null)
        => RequestStep.Rehydrate(
            Guid.CreateVersion7(),
            RequestId,
            StepName,
            status,
            MessageType,
            JsonDocument.Parse("""{"attempt":"persisted"}"""),
            stepRetries: 0,
            nextRetryAt: null,
            lockedBy: null,
            lockedUntil: null,
            startedAt: startedAt,
            completedAt: completedAt,
            result: result,
            errorCode: errorCode,
            errorMessage: errorCode is null ? null : "HEAppE timed out");

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

    private static JsonDocument Result(int jobId) => JsonDocument.Parse($$"""{"jobId":{{jobId}}}""");

    private static int? JobIdOf(RequestStep step)
        => step.Result?.RootElement.GetProperty("jobId").GetInt32();

    private static string Describe(int? jobId) => jobId is null ? "no result" : $"jobId {jobId}";
}
