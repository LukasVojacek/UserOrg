using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Tests.Helpers;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Workers;

/// <summary>
/// What a restart does to a step's retry budget, driven through the real worker, the real
/// <see cref="RequestWorkerStartupRecovery"/> and a real <c>WorkflowDbContextRepository</c> over SQLite
/// (<see cref="RetryBudgetHarness"/>).
///
/// <para>
/// T-121-04 C1: <c>RequestStep.ReopenForRetry</c> resets <c>StepRetries</c> so that an operator's retry
/// starts the budget again — a decision about the operator path. But
/// <c>RequestAggregate.ResetStepForRetry</c> has a third caller,
/// <see cref="RequestWorkerStartupRecovery"/>, which re-enqueues a step whose worker died mid-attempt.
/// A crash loop therefore hands the same step a full budget on every restart, which is F1 — the unbounded
/// retry of the incident — reachable again through the recovery path.
/// </para>
///
/// <para>
/// The crash is written straight onto the row (<c>StallLiveStepAsync</c>): a worker persists
/// <c>Running</c> plus its lease before invoking the handler, and a killed process leaves exactly that
/// behind. No domain method produces it, because no domain method models a process dying.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class StartupRecoveryRetryBudgetTests
{
    private const string PoisonStepName = nameof(RetryBudgetHarness.PoisonStep);

    /// <summary>Generous for a handler that throws immediately; the recoveries need milliseconds.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Fact(DisplayName = "A stalled step re-enqueued by startup recovery keeps the retries it had already spent")]
    public async Task Should_KeepTheSpentRetries_When_StartupRecoveryReEnqueuesAStalledStep()
    {
        const int maxStepRetries = 3;
        const int spentBeforeTheCrash = 2;

        // why: a budget of 3 leaves the recovered delivery room to spend a retry instead of exhausting the budget, so the attempt count it lands on stays readable in the row afterwards — 3 when recovery carried the spent retries forward, 1 when it started the budget again.
        await using var harness = await RetryBudgetHarness.CreateAsync(
            maxStepRetries: maxStepRetries,
            retryBackoffBase: TimeSpan.FromSeconds(10),
            withScanner: false,
            ct: TestContext.Current.CancellationToken);

        var requestId = await harness.SubmitPoisonAsync();
        await SpendTwoRetriesAsync(harness, requestId);

        Assert.True(
            await WaitForQuietStepAsync(harness, requestId),
            $"The worker never finished with the step: the request is {Describe(await harness.LoadAsync(requestId))}, so there was no settled row to stall.");

        Assert.True(
            await harness.StallLiveStepAsync(requestId, PoisonStepName),
            $"The step had no live row to leave mid-attempt, so the crash this test recovers from never happened. Request: {Describe(await harness.LoadAsync(requestId))}.");

        await harness.RunStartupRecoveryAsync();

        await RetryBudgetHarness.WaitForAsync(
            async () => RetryBudgetHarness.PoisonInvocations(requestId) > spentBeforeTheCrash
                        && LiveStep(await harness.LoadAsync(requestId))?.ExecutionStatus
                            is StepExecutionStatus.RetryScheduled or StepExecutionStatus.Failed,
            Deadline);

        var aggregate = await harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        var step = LiveStep(aggregate);
        Assert.NotNull(step);

        Assert.True(
            step.StepRetries == spentBeforeTheCrash + 1,
            $"After startup recovery re-enqueued the stalled step, the live row records StepRetries = {step.StepRetries}. The step had spent {spentBeforeTheCrash} retries before the worker died mid-attempt and the recovered delivery spends one more, so it must be {spentBeforeTheCrash + 1}. A value of 1 means RequestWorkerStartupRecovery's ResetStepForRetry restarted the budget: the crashed step is back to a full MaxStepRetries = {maxStepRetries}. Request: {Describe(aggregate)}.");
    }

    [Fact(DisplayName = "A worker crashing mid-attempt on every restart cannot push the handler past MaxStepRetries + 1 invocations")]
    public async Task Should_NotRestartTheBudget_When_ACrashLoopKeepsBeingRecoveredAtStartup()
    {
        const int maxStepRetries = 1;
        const int allowed = maxStepRetries + 1;
        const int crashCycles = 3;

        await using var harness = await RetryBudgetHarness.CreateAsync(
            maxStepRetries: maxStepRetries,
            retryBackoffBase: TimeSpan.FromSeconds(10),
            withScanner: false,
            ct: TestContext.Current.CancellationToken);

        var requestId = await harness.SubmitPoisonAsync();

        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, 1, Deadline),
            "The first delivery never reached the handler.");

        var cyclesRun = 0;

        for (var cycle = 0; cycle < crashCycles; cycle++)
        {
            Assert.True(
                await WaitForQuietStepAsync(harness, requestId),
                $"Cycle {cycle + 1}: the worker never finished with the step. Request: {Describe(await harness.LoadAsync(requestId))}.");

            // why: no live row left means the step is terminal and a restart has nothing to recover — the crash loop has ended, which is the outcome this test is here to establish.
            if (!await harness.StallLiveStepAsync(requestId, PoisonStepName))
            {
                break;
            }

            var before = RetryBudgetHarness.PoisonInvocations(requestId);
            await harness.RunStartupRecoveryAsync();
            cyclesRun++;

            await RetryBudgetHarness.WaitForAsync(
                async () => RetryBudgetHarness.PoisonInvocations(requestId) > before
                            || (await harness.LoadAsync(requestId))?.Status is RequestStatus.Failed,
                Deadline);
        }

        var invocations = RetryBudgetHarness.PoisonInvocations(requestId);

        Assert.True(
            invocations <= allowed,
            $"The poison handler ran {invocations} times for MaxStepRetries = {maxStepRetries} across {cyclesRun} crash-and-recover cycle(s); the budget allows {allowed} (the initial attempt plus {maxStepRetries} retry). RequestWorkerStartupRecovery re-enqueues a stalled step through ResetStepForRetry, which clears StepRetries, so every restart returns the crashed step to a full budget and a worker that keeps dying mid-attempt retries without bound.");

        var aggregate = await harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        var step = LiveStep(aggregate);
        Assert.NotNull(step);

        Assert.True(
            step.ExecutionStatus == StepExecutionStatus.Failed && step.StepRetries == maxStepRetries,
            $"After {cyclesRun} crash-and-recover cycle(s) the step is {step.ExecutionStatus} with StepRetries = {step.StepRetries}; the recovered delivery spends the last retry of the original budget, so the row must be Failed with StepRetries = {maxStepRetries}. RetryScheduled means the recovered step was handed a new budget and is waiting to spend it.");

        Assert.True(
            aggregate.Status == RequestStatus.Failed,
            $"The request is {aggregate.Status} after {invocations} handler invocations across {cyclesRun} crash-and-recover cycle(s); a step whose budget is spent must close its request as Failed.");
    }

    /// <summary>
    /// Drives two deliveries of the always-throwing step by hand — the scanner is off — and returns once
    /// both retries are persisted, leaving the step two attempts into its budget.
    /// </summary>
    private static async Task SpendTwoRetriesAsync(RetryBudgetHarness harness, Guid requestId)
    {
        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, 1, Deadline),
            "The first delivery never reached the handler.");
        Assert.True(
            await harness.WaitForRecordedRetriesAsync(requestId, PoisonStepName, 1, Deadline),
            "The first retry was never recorded.");

        await harness.RedeliverPoisonStepAsync(requestId);

        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, 2, Deadline),
            "The second delivery never reached the handler.");
        Assert.True(
            await harness.WaitForRecordedRetriesAsync(requestId, PoisonStepName, 2, Deadline),
            "The second retry was never recorded.");
    }

    /// <summary>
    /// Waits until the worker has finished with the step and let go of its lease. The row has to be settled
    /// before it is stalled: <c>ReleaseStepLeaseAsync</c> runs after the save that records the retry, so a
    /// crash written in between would have the release clear the expired lease again — and
    /// <c>FindStalledAsync</c> matches on <c>LockedUntil &lt; cutoff</c>, which <c>null</c> never is.
    /// </summary>
    private static Task<bool> WaitForQuietStepAsync(RetryBudgetHarness harness, Guid requestId)
        => RetryBudgetHarness.WaitForAsync(
            async () =>
            {
                var step = LiveStep(await harness.LoadAsync(requestId));
                return step is not null
                    && step.LockedUntil is null
                    && step.ExecutionStatus is StepExecutionStatus.RetryScheduled or StepExecutionStatus.Failed;
            },
            Deadline);

    private static RequestStep? LiveStep(RequestAggregate? aggregate)
        => aggregate?.FindLiveStep(PoisonStepName);

    private static string Describe(RequestAggregate? aggregate)
        => aggregate is null
            ? "missing"
            : $"{aggregate.Status} [" + string.Join(
                ", ",
                aggregate.Steps.Select(s =>
                    $"{s.StepName}:{s.ExecutionStatus}(retries={s.StepRetries},lockedUntil={s.LockedUntil:O})")) + "]";
}
