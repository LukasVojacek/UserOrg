using System.Text.Json;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Tests.Helpers;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Workers;

/// <summary>
/// A <c>RetryScheduled</c> step whose stored message cannot be turned back into a <c>StepRequest</c> is
/// poison: <see cref="RetryScheduledScanner"/> logs the failure, releases the lease and moves on, the row
/// stays <c>RetryScheduled</c>, and <c>FindDueRetriesAsync</c> hands it straight back on the next poll —
/// every <c>RetryPollingInterval</c> (2 s by default; no <c>WorkflowEngine</c> section exists in any
/// appsettings, so the default is what production runs), for as long as the process lives. No retry budget,
/// no path to <c>Failed</c>: an unbounded log flood and permanent queue churn.
/// <c>docs/architecture/adr/0067-workflow-step-claim-invariant.md</c> § Consequences records this same shape
/// as a known, deliberately deferred limitation ("the scanner re-enqueues the stale row indefinitely …
/// permanent queue churn"). These tests close it.
///
/// <para>
/// What is pinned here is the <em>bound</em>, not a particular threshold: after a bounded number of
/// consecutive failures to materialise the message the step must end <see cref="StepExecutionStatus.Failed"/>
/// and stop being selected. Which number, and whether it is a named constant or a
/// <c>WorkflowEngineOptions</c> setting, is the implementation's choice — the tests only insist it is small
/// and finite (<see cref="MaxToleratedFailureLogs"/>), never "once per poll forever".
/// </para>
///
/// <para>
/// The cases cover every cause the scanner can hit, not just the payload shape that motivated the fix:
/// a payload that throws while deserializing (a renamed or retyped DTO property), a payload that
/// deserializes to <c>null</c>, and a <c>MessageType</c> that no longer resolves (a type renamed or moved
/// across a deploy). All three leave the row in the same unbounded loop today, and both flows registered in
/// the harness are exercised, because the bound belongs to the scanner and not to one strategy.
/// </para>
/// </summary>
public sealed class RetryScheduledScannerPoisonPayloadTests : IAsyncLifetime
{
    /// <summary>
    /// A generous ceiling on how often the scanner may log the same unrecoverable step before it gives up.
    /// The point is the order of magnitude: a bounded retry budget logs a handful of times, the current
    /// scanner logs once per poll for the lifetime of the process.
    /// </summary>
    private const int MaxToleratedFailureLogs = 10;

    /// <summary>Roughly 60 polls at the harness interval — far more than any sane retry budget.</summary>
    private static readonly TimeSpan FailureDeadline = TimeSpan.FromSeconds(3);

    /// <summary>Long enough for a dozen further polls, to prove nothing picks the row up again.</summary>
    private static readonly TimeSpan SettleWindow = TimeSpan.FromMilliseconds(600);

    private const string MalformedJson = "malformed-json";
    private const string DeserializesToNull = "deserializes-to-null";
    private const string UnresolvableMessageType = "unresolvable-message-type";

    private RetryScannerHarness _harness = null!;

    public async ValueTask InitializeAsync()
        => _harness = await RetryScannerHarness.CreateAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _harness.DisposeAsync();

    [Theory(DisplayName = "A step whose message cannot be materialised ends Failed instead of being rescanned forever")]
    [InlineData(RetryScannerHarness.AlphaStrategy, MalformedJson)]
    [InlineData(RetryScannerHarness.AlphaStrategy, DeserializesToNull)]
    [InlineData(RetryScannerHarness.AlphaStrategy, UnresolvableMessageType)]
    [InlineData(RetryScannerHarness.BravoStrategy, MalformedJson)]
    public async Task Should_FailTheStep_When_TheScannerCannotMaterialiseItsMessage(string strategy, string cause)
    {
        var requestId = await SeedPoisonStepAsync(strategy, cause);

        await using (await _harness.StartScannerAsync())
        {
            var failed = await RetryScannerHarness.WaitForAsync(
                async () => (await _harness.LoadAsync(requestId))?.Status == RequestStatus.Failed,
                FailureDeadline);

            var aggregate = await _harness.LoadAsync(requestId);
            Assert.NotNull(aggregate);

            Assert.True(
                failed,
                $"The poison step ({cause}) was never failed. The request is still {aggregate.Status}/{aggregate.Steps.Single().ExecutionStatus} after {FailureDeadline}, and the scanner has logged {_harness.Logs.CountFailureLogsMentioning(requestId.ToString())} failure entries (Error or Warning) about it — one per poll, with no path out of RetryScheduled.");

            var step = Assert.Single(aggregate.Steps);
            Assert.Equal(StepExecutionStatus.Failed, step.ExecutionStatus);
            Assert.False(
                string.IsNullOrWhiteSpace(step.ErrorCode),
                "A step failed by the scanner must carry an error code an operator can act on.");

            Assert.True(
                _harness.Logs.CountFailureLogsMentioning(requestId.ToString()) <= MaxToleratedFailureLogs,
                $"The scanner logged {_harness.Logs.CountFailureLogsMentioning(requestId.ToString())} failure entries for one unrecoverable step; at most {MaxToleratedFailureLogs} is a bounded budget.");

            // Nothing may reach the queue: the whole point is that no message could be built.
            Assert.Empty(_harness.Queue.Messages);

            // And the row must be gone from the scanner's own selection, not merely quiet for a moment.
            Assert.Equal(0, await _harness.CountDueRetriesAsync(requestId));

            var afterFailure = _harness.Logs.CountFailureLogsMentioning(requestId.ToString());
            await Task.Delay(SettleWindow, TestContext.Current.CancellationToken);

            Assert.Equal(afterFailure, _harness.Logs.CountFailureLogsMentioning(requestId.ToString()));
        }
    }

    [Fact(DisplayName = "The scanner logs an unrecoverable step a bounded number of times, not once per poll")]
    public async Task Should_StopLogging_When_TheSameStepKeepsFailingToDeserialize()
    {
        // This is the flood itself, measured without reference to any status transition: let the scanner
        // poll freely for a fixed window and count. Today the count tracks the number of polls exactly.
        var requestId = await SeedPoisonStepAsync(RetryScannerHarness.AlphaStrategy, MalformedJson);

        var window = TimeSpan.FromSeconds(2);
        var possiblePolls = (int)(window / RetryScannerHarness.PollInterval);

        await using (await _harness.StartScannerAsync())
        {
            await Task.Delay(window, TestContext.Current.CancellationToken);
        }

        var logged = _harness.Logs.CountFailureLogsMentioning(requestId.ToString());

        Assert.True(
            logged <= MaxToleratedFailureLogs,
            $"The scanner logged {logged} failure entries about the same unrecoverable step in {window} (~{possiblePolls} polls). The log volume must be bounded by a retry budget, not by uptime.");
    }

    /// <summary>
    /// The scanner fails the exact row it was handed and closes only a request that is actually sitting on
    /// that step; a request making progress on a later state keeps going.
    ///
    /// <para>
    /// Before T-121 this scenario was two rows sharing one StepName — the stale <c>RetryScheduled</c> row and
    /// the <c>Completed</c> row that superseded it — because <c>RecordStepStarted</c> appended one row per
    /// delivery. That pair is the defect, not the contract: a retried step now keeps one live row per step
    /// name, so the same scenario has one row and the assertions below say so.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "A due poison row on a step the flow has moved past is failed on its own row, without failing the request")]
    public async Task Should_FailOnlyTheDueRow_When_TheFlowHasMovedPastIt()
    {
        const string advancedState = "LaterStep";
        var requestId = await _harness.SeedDueRowOnAStepTheFlowHasMovedPastAsync(
            RetryScannerHarness.AlphaStrategy,
            nameof(RetryScannerHarness.AlphaStep),
            typeof(RetryScannerHarness.AlphaStep).AssemblyQualifiedName!,
            JsonDocument.Parse("""{"RequestId":"not-a-guid"}"""),
            advancedState);

        await using (await _harness.StartScannerAsync())
        {
            var settled = await RetryScannerHarness.WaitForAsync(
                async () => (await _harness.LoadAsync(requestId))?.Steps
                    .Any(s => s.ExecutionStatus == StepExecutionStatus.Failed) == true,
                FailureDeadline);

            var aggregate = await _harness.LoadAsync(requestId);
            Assert.NotNull(aggregate);

            Assert.True(
                settled,
                $"The poison row is still due after {FailureDeadline}. The scanner logged {_harness.Logs.CountFailureLogsMentioning(requestId.ToString())} failure entries about it, which means the bound is being applied to a row other than the one that keeps being selected.");

            Assert.Equal(0, await _harness.CountDueRetriesAsync(requestId));

            var failed = Assert.Single(aggregate.Steps);
            Assert.Equal(StepExecutionStatus.Failed, failed.ExecutionStatus);
            Assert.Equal(1, failed.StepRetries);
            Assert.False(string.IsNullOrWhiteSpace(failed.ErrorCode));
            Assert.NotEqual("TRANSIENT", failed.ErrorCode);

            Assert.Equal(RequestStatus.InProgress, aggregate.Status);
            Assert.Equal(advancedState, aggregate.CurrentState);

            var afterFailure = _harness.Logs.CountFailureLogsMentioning(requestId.ToString());
            await Task.Delay(SettleWindow, TestContext.Current.CancellationToken);
            Assert.Equal(afterFailure, _harness.Logs.CountFailureLogsMentioning(requestId.ToString()));
        }
    }

    /// <summary>
    /// Seeds a due <c>RetryScheduled</c> step the scanner cannot turn into a message, for one of the three
    /// causes. The step name and message type follow the strategy, so each row is a plausible row of a real
    /// flow rather than a synthetic one.
    /// </summary>
    private Task<Guid> SeedPoisonStepAsync(string strategy, string cause)
    {
        var alpha = strategy == RetryScannerHarness.AlphaStrategy;
        var stepName = alpha ? nameof(RetryScannerHarness.AlphaStep) : nameof(RetryScannerHarness.BravoStep);
        var messageType = (alpha
            ? typeof(RetryScannerHarness.AlphaStep)
            : typeof(RetryScannerHarness.BravoStep)).AssemblyQualifiedName!;

        var (type, payload) = cause switch
        {
            // A property that no longer holds the value it used to — the DTO drift the fix must survive.
            MalformedJson => (messageType, JsonDocument.Parse("""{"RequestId":"not-a-guid"}""")),

            // Valid JSON, deserializes cleanly, yields no message at all.
            DeserializesToNull => (messageType, JsonDocument.Parse("null")),

            // The type was renamed or moved between the row being written and this process starting.
            UnresolvableMessageType => (
                "WorkflowEngine.Core.Tests.Removed.RenamedStep, WorkflowEngine.Removed",
                JsonDocument.Parse($$"""{"RequestId":"{{Guid.Empty}}"}""")),

            _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "unknown poison cause"),
        };

        return _harness.SeedRetryScheduledStepAsync(strategy, stepName, type, payload);
    }
}
