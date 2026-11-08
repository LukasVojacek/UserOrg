using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Tests.Helpers;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Workers;

/// <summary>
/// The retry budget, driven through the real worker, the real <see cref="RetryScheduledScanner"/> and a real
/// <c>WorkflowDbContextRepository</c> over SQLite (<see cref="RetryBudgetHarness"/>).
///
/// <para>
/// T-121: a step whose handler keeps throwing <c>RetryableException</c> was invoked without bound in
/// production — 1008 executions of one step, and an unrelated request stranded in <c>Accepted</c> behind the
/// flood — because <c>RecordStepStarted</c> appends a step row with <c>StepRetries = 0</c> before every
/// delivery and both the budget guard and the back-off read that fresh row through <c>GetLastStep()</c>. The
/// superseded row keeps <c>RetryScheduled</c> with an elapsed <c>NextRetryAt</c>, so <c>FindDueRetriesAsync</c>
/// hands it back on every poll for the life of the process.
/// </para>
///
/// <para>
/// Every wait here is bounded twice over: by a deadline, and — for the invocation count — by a ceiling that
/// ends the wait the moment the handler runs once more than the budget allows. Nothing here may wait for a
/// loop that, on the unfixed code, does not end.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class StepRetryBudgetTests
{
    private const string PoisonStepName = nameof(RetryBudgetHarness.PoisonStep);

    /// <summary>Generous for a handler that throws immediately; the flood needs milliseconds, not seconds.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [Fact(DisplayName = "A step whose handler always throws RetryableException is invoked MaxStepRetries + 1 times, then fails")]
    public async Task Should_StopAfterTheBudget_When_TheHandlerAlwaysThrowsRetryable()
    {
        const int maxStepRetries = 1;
        const int allowed = maxStepRetries + 1;

        // why: the scanner is off and the one redelivery is driven by hand, as in the tests below — with it
        // running it re-enqueues the due row every poll until the worker persists Running, so a duplicate
        // delivery could push the count past the budget and fail this assertion for a reason that is not the
        // defect.
        await using var harness = await RetryBudgetHarness.CreateAsync(
            maxStepRetries: maxStepRetries,
            retryBackoffBase: TimeSpan.FromMilliseconds(250),
            withScanner: false,
            ct: TestContext.Current.CancellationToken);

        var requestId = await harness.SubmitPoisonAsync();

        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, 1, Deadline),
            "The first delivery never reached the handler.");
        Assert.True(
            await harness.WaitForRecordedRetriesAsync(requestId, PoisonStepName, 1, Deadline),
            "The first retry was never recorded.");

        await harness.RedeliverPoisonStepAsync(requestId);

        await RetryBudgetHarness.WaitForAsync(
            async () => RetryBudgetHarness.PoisonInvocations(requestId) > allowed
                        || (await harness.LoadAsync(requestId))?.Status is RequestStatus.Failed,
            Deadline);

        var invocations = RetryBudgetHarness.PoisonInvocations(requestId);

        Assert.True(
            invocations <= allowed,
            $"The handler was invoked {invocations} times for MaxStepRetries = {maxStepRetries}; the budget allows {allowed} (the initial attempt plus {maxStepRetries} retry). The wait stopped counting at the first invocation over budget — the loop itself is unbounded.");

        var failed = await harness.WaitForStatusAsync(requestId, RequestStatus.Failed, Deadline);
        var aggregate = await harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        Assert.True(
            failed,
            $"The request never reached Failed: it is {aggregate.Status} after {invocations} handler invocations, with steps [{Describe(aggregate.Steps)}].");

        var step = Assert.Single(aggregate.Steps);
        Assert.Equal(PoisonStepName, step.StepName);
        Assert.Equal(StepExecutionStatus.Failed, step.ExecutionStatus);
        Assert.Equal(maxStepRetries, step.StepRetries);
        Assert.Equal(allowed, RetryBudgetHarness.PoisonInvocations(requestId));
    }

    [Fact(DisplayName = "A step retried through RetryHandler gets a fresh budget: the handler runs MaxStepRetries + 1 times again")]
    public async Task Should_GiveAFreshBudget_When_AnOperatorRetriesTheFailedStep()
    {
        const int maxStepRetries = 1;
        const int allowed = maxStepRetries + 1;

        await using var harness = await RetryBudgetHarness.CreateAsync(
            maxStepRetries: maxStepRetries,
            retryBackoffBase: TimeSpan.FromMilliseconds(250),
            withScanner: false,
            ct: TestContext.Current.CancellationToken);

        var requestId = await ExhaustedBudgetAsync(harness, maxStepRetries);
        var beforeRetry = RetryBudgetHarness.PoisonInvocations(requestId);

        await harness.RetryLastStepThroughRetryHandlerAsync(requestId);

        // why: the retry enqueues the step itself; every further attempt of the fresh budget is redelivered
        // by hand, one per RetryScheduled, up to one delivery more than the budget allows so an over-budget
        // regression is counted rather than waited out.
        await DrainScheduledRetriesAsync(harness, requestId, deliveryCap: allowed);

        var aggregate = await harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        var afterRetry = RetryBudgetHarness.PoisonInvocations(requestId) - beforeRetry;

        Assert.True(
            afterRetry == allowed,
            $"The operator retry gave the step {afterRetry} handler invocation(s); a manual retry starts the budget again, so it must give {allowed} (the initial attempt plus {maxStepRetries} retry). A count of 1 means ResetStepForRetry re-opened the live row without clearing StepRetries, so IsRetryBudgetExhausted was already true on the retried delivery. Steps after the retry: [{Describe(aggregate.Steps)}].");

        var step = Assert.Single(aggregate.Steps);
        Assert.Equal(PoisonStepName, step.StepName);

        Assert.True(
            step.ExecutionStatus == StepExecutionStatus.Failed && step.StepRetries == maxStepRetries,
            $"After the retried budget the step is {step.ExecutionStatus} with StepRetries = {step.StepRetries}; it must be Failed with StepRetries = {maxStepRetries}, the same shape the first budget ended in.");

        Assert.Equal(RequestStatus.Failed, aggregate.Status);
    }

    [Fact(DisplayName = "FindDueRetriesAsync returns at most one row per (RequestId, StepName) once a step has retried")]
    public async Task Should_LeaveOneDueRowPerStep_When_AStepHasRetried()
    {
        await using var harness = await RetryBudgetHarness.CreateAsync(
            maxStepRetries: 3,
            retryBackoffBase: TimeSpan.FromSeconds(10),
            withScanner: false,
            ct: TestContext.Current.CancellationToken);

        var requestId = await TwoFailedAttemptsAsync(harness);

        // why: an hour ahead makes every scheduled row due, whatever back-off it was given.
        var due = await harness.DueRetriesAsync(DateTimeOffset.UtcNow.AddHours(1));

        var rowsForThisStep = due.Count(entry => entry.RequestId == requestId && entry.StepName == PoisonStepName);

        Assert.True(
            rowsForThisStep <= 1,
            $"FindDueRetriesAsync returned {rowsForThisStep} due rows for ({requestId}, {PoisonStepName}) after two failed attempts. Every row past the first is a row nothing will ever flip, so the scanner re-enqueues it on every poll for the life of the process.");
    }

    [Fact(DisplayName = "The second retry waits RetryBackoffBase * 2, not RetryBackoffBase")]
    public async Task Should_DoubleTheBackoff_When_TheSecondRetryIsScheduled()
    {
        var backoffBase = TimeSpan.FromSeconds(10);
        var expected = backoffBase * 2;

        await using var harness = await RetryBudgetHarness.CreateAsync(
            maxStepRetries: 3,
            retryBackoffBase: backoffBase,
            withScanner: false,
            ct: TestContext.Current.CancellationToken);

        var requestId = await harness.SubmitPoisonAsync();

        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, 1, Deadline),
            "The first delivery never reached the handler.");
        Assert.True(
            await harness.WaitForRecordedRetriesAsync(requestId, PoisonStepName, 1, Deadline),
            "The first retry was never recorded.");

        var beforeSecondDelivery = DateTimeOffset.UtcNow;
        await harness.RedeliverPoisonStepAsync(requestId);

        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, 2, Deadline),
            "The second delivery never reached the handler.");
        Assert.True(
            await harness.WaitForRecordedRetriesAsync(requestId, PoisonStepName, 2, Deadline),
            "The second retry was never recorded.");

        var aggregate = await harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        var scheduled = aggregate.Steps
            .Where(s => s.StepName == PoisonStepName && s.NextRetryAt is not null)
            .MaxBy(s => s.NextRetryAt);
        Assert.NotNull(scheduled);

        var delay = scheduled.NextRetryAt!.Value - beforeSecondDelivery;

        Assert.True(
            delay >= expected - TimeSpan.FromSeconds(2) && delay <= expected + TimeSpan.FromSeconds(5),
            $"The second retry was scheduled {delay.TotalSeconds:F1}s out; RetryBackoffBase * 2^(2-1) is {expected.TotalSeconds:F1}s. A delay of about {backoffBase.TotalSeconds:F1}s means the back-off counted this step's attempts off a row that had none. Steps: [{Describe(aggregate.Steps)}].");
    }

    [Fact(DisplayName = "A request submitted while another exhausts its retry budget still completes")]
    public async Task Should_CompleteTheUnrelatedRequest_When_AnotherStepIsExhaustingItsBudget()
    {
        await using var harness = await RetryBudgetHarness.CreateAsync(
            maxStepRetries: 1,
            retryBackoffBase: TimeSpan.FromMilliseconds(100),
            retryPollingInterval: TimeSpan.FromMilliseconds(25),
            queueCapacity: 4,
            ct: TestContext.Current.CancellationToken);

        var poisonId = await harness.SubmitPoisonAsync();

        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(poisonId, 2, Deadline),
            "The poison request never got as far as its first retry, so nothing was competing for the queue.");

        var healthyId = await harness.SubmitHealthyAsync();

        var completed = await harness.WaitForStatusAsync(healthyId, RequestStatus.Completed, Deadline);
        var healthy = await harness.LoadAsync(healthyId);

        Assert.True(
            completed,
            $"The unrelated request is {healthy?.Status.ToString() ?? "missing"} after {Deadline.TotalSeconds:F0}s, while the poison step ran {RetryBudgetHarness.PoisonInvocations(poisonId)} times. This is the incident: a request submitted behind an unbounded retry loop is dropped by the bounded queue and never moves.");

        var poisonFailed = await harness.WaitForStatusAsync(poisonId, RequestStatus.Failed, Deadline);
        var poison = await harness.LoadAsync(poisonId);

        Assert.True(
            poisonFailed,
            $"The poison request is {poison?.Status.ToString() ?? "missing"} after {RetryBudgetHarness.PoisonInvocations(poisonId)} handler invocations with MaxStepRetries = 1, and its steps are [{Describe(poison?.Steps ?? [])}]. This is the only budget test that leaves the scanner running, so it is the only place the loop of the incident — worker, scanner and repository, with nothing redelivered by hand — is pinned as terminating.");
    }

    /// <summary>
    /// Submits the always-throwing step and drives it, by hand, to the end of its first budget: the step is
    /// <c>Failed</c>, the request is <c>Failed</c> and the handler has run <c>maxStepRetries + 1</c> times.
    /// </summary>
    private static async Task<Guid> ExhaustedBudgetAsync(RetryBudgetHarness harness, int maxStepRetries)
    {
        var requestId = await harness.SubmitPoisonAsync();

        Assert.True(
            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, 1, Deadline),
            "The first delivery never reached the handler.");
        Assert.True(
            await harness.WaitForRecordedRetriesAsync(requestId, PoisonStepName, 1, Deadline),
            "The first retry was never recorded.");

        await DrainScheduledRetriesAsync(harness, requestId, deliveryCap: maxStepRetries + 1);

        Assert.True(
            await harness.WaitForStatusAsync(requestId, RequestStatus.Failed, Deadline),
            $"The first budget never ended: the request is still open after {RetryBudgetHarness.PoisonInvocations(requestId)} handler invocations.");

        return requestId;
    }

    /// <summary>
    /// Redelivers the poison step by hand once per <c>RetryScheduled</c> outcome — what the scanner would do
    /// once the retry falls due — and stops as soon as the step reaches a terminal status or
    /// <paramref name="deliveryCap"/> deliveries have been made. The cap is what keeps a budget that never
    /// exhausts from being waited out: the caller's count assertion reports it instead.
    /// </summary>
    private static async Task DrainScheduledRetriesAsync(RetryBudgetHarness harness, Guid requestId, int deliveryCap)
    {
        for (var delivery = 0; delivery < deliveryCap; delivery++)
        {
            StepExecutionStatus? outcome = null;

            await RetryBudgetHarness.WaitForAsync(
                async () =>
                {
                    var aggregate = await harness.LoadAsync(requestId);
                    outcome = aggregate?.Steps
                        .LastOrDefault(s => s.StepName == PoisonStepName
                                         && s.ExecutionStatus != StepExecutionStatus.Completed)
                        ?.ExecutionStatus;

                    return outcome is StepExecutionStatus.RetryScheduled or StepExecutionStatus.Failed;
                },
                Deadline);

            if (outcome != StepExecutionStatus.RetryScheduled)
            {
                return;
            }

            var before = RetryBudgetHarness.PoisonInvocations(requestId);
            await harness.RedeliverPoisonStepAsync(requestId);

            await RetryBudgetHarness.WaitForPoisonAttemptsAsync(requestId, before + 1, Deadline);
        }
    }

    /// <summary>
    /// Drives two deliveries of the always-throwing step and returns once both retries are persisted, using
    /// the worker only — the scanner is off, so the number of deliveries is this method's decision.
    /// </summary>
    private static async Task<Guid> TwoFailedAttemptsAsync(RetryBudgetHarness harness)
    {
        var requestId = await harness.SubmitPoisonAsync();

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

        return requestId;
    }

    private static string Describe(IEnumerable<Core.Domain.Request.RequestStep> steps)
        => string.Join(
            ", ",
            steps.Select(s => $"{s.StepName}:{s.ExecutionStatus}(retries={s.StepRetries},next={s.NextRetryAt:O})"));
}
