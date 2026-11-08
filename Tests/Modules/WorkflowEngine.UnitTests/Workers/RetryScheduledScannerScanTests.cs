using System.Text.Json;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Tests.Helpers;
using WorkflowEngine.Core.Workers;

namespace WorkflowEngine.Core.Tests.Workers;

/// <summary>
/// Characterization of what <see cref="RetryScheduledScanner"/> does with the rows it scans <em>today</em>,
/// captured before the scanner grows a bound on unrecoverable steps
/// (<see cref="RetryScheduledScannerPoisonPayloadTests"/>). Each of these must still hold afterwards:
/// the selection rules (due / not due), the lease refusal that keeps a second instance off a step another
/// worker owns, and the fact that one bad row in a batch does not stop the good rows behind it.
///
/// <para>
/// <c>ScheduledRetryExecutionTests</c> covers the same scanner together with <c>RequestWorker</c> and the
/// full retry execution; this file deliberately runs the scanner alone, so a failure points at the scanner
/// rather than at the worker.
/// </para>
/// </summary>
public sealed class RetryScheduledScannerScanTests : IAsyncLifetime
{
    /// <summary>~12 polls at the harness interval — enough for "it never happens" to mean something.</summary>
    private static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan EnqueueDeadline = TimeSpan.FromSeconds(3);

    private RetryScannerHarness _harness = null!;

    public async ValueTask InitializeAsync()
        => _harness = await RetryScannerHarness.CreateAsync(TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _harness.DisposeAsync();

    [Fact(DisplayName = "A due retry is re-enqueued and the scanner keeps the lease it took")]
    public async Task Should_EnqueueAndHoldTheLease_When_TheRetryIsDue()
    {
        var requestId = await SeedHealthyStepAsync();

        await using (await _harness.StartScannerAsync())
        {
            var enqueued = await RetryScannerHarness.WaitForAsync(
                () => Task.FromResult(_harness.Queue.Messages.Count > 0), EnqueueDeadline);

            Assert.True(enqueued, "The scanner did not re-enqueue a due retry.");
        }

        var message = Assert.IsType<RetryScannerHarness.AlphaStep>(_harness.Queue.Messages[0]);
        Assert.Equal(requestId, message.RequestId);

        var aggregate = await _harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        // The scanner does not settle the step — that is the worker's job. It only leases and enqueues,
        // and it keeps the lease (ADR-0067 amendment: the lease is also the scanner's de-dup).
        var step = Assert.Single(aggregate.Steps);
        Assert.Equal(StepExecutionStatus.RetryScheduled, step.ExecutionStatus);
        Assert.Equal(RetryScannerHarness.WorkerId, step.LockedBy);
        Assert.NotNull(step.LockedUntil);
    }

    [Fact(DisplayName = "A retry that is not yet due is left alone, unleased and unqueued")]
    public async Task Should_LeaveTheStepAlone_When_TheRetryIsNotYetDue()
    {
        var requestId = await SeedHealthyStepAsync(dueIn: TimeSpan.FromMinutes(5));

        await using (await _harness.StartScannerAsync())
        {
            await Task.Delay(QuietWindow, TestContext.Current.CancellationToken);
        }

        Assert.Empty(_harness.Queue.Messages);

        var aggregate = await _harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        var step = Assert.Single(aggregate.Steps);
        Assert.Equal(StepExecutionStatus.RetryScheduled, step.ExecutionStatus);
        Assert.Null(step.LockedBy);
    }

    [Fact(DisplayName = "A due retry another worker holds a live lease on is skipped, lease untouched")]
    public async Task Should_SkipTheStep_When_AnotherWorkerHoldsALiveLease()
    {
        const string otherWorker = "another-instance";

        var requestId = await SeedHealthyStepAsync(
            lockedBy: otherWorker,
            lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await using (await _harness.StartScannerAsync())
        {
            await Task.Delay(QuietWindow, TestContext.Current.CancellationToken);
        }

        Assert.Empty(_harness.Queue.Messages);

        var aggregate = await _harness.LoadAsync(requestId);
        Assert.NotNull(aggregate);

        var step = Assert.Single(aggregate.Steps);
        Assert.Equal(StepExecutionStatus.RetryScheduled, step.ExecutionStatus);
        Assert.Equal(otherWorker, step.LockedBy);
    }

    [Fact(DisplayName = "A step the scanner cannot deserialize does not stop the healthy steps in the same scan")]
    public async Task Should_EnqueueTheHealthyStep_When_APoisonStepIsDueInTheSameScan()
    {
        // This is the property the bound must not cost: whatever the scanner does about the poison row, the
        // rest of the batch still has to move. `foreach` order is the repository's, so the poison row may
        // well come first.
        await _harness.SeedRetryScheduledStepAsync(
            RetryScannerHarness.BravoStrategy,
            nameof(RetryScannerHarness.BravoStep),
            typeof(RetryScannerHarness.BravoStep).AssemblyQualifiedName!,
            JsonDocument.Parse("""{"RequestId":"not-a-guid"}"""));

        var healthyId = await SeedHealthyStepAsync();

        await using (await _harness.StartScannerAsync())
        {
            var enqueued = await RetryScannerHarness.WaitForAsync(
                () => Task.FromResult(_harness.Queue.Messages.Any(m => m.RequestId == healthyId)),
                EnqueueDeadline);

            Assert.True(enqueued, "The poison step swallowed the scan: the healthy retry was never enqueued.");
        }

        Assert.All(_harness.Queue.Messages, message => Assert.Equal(healthyId, message.RequestId));
    }

    /// <summary>A due <c>AlphaStep</c> retry whose stored payload is exactly what the engine wrote.</summary>
    private Task<Guid> SeedHealthyStepAsync(
        TimeSpan? dueIn = null,
        string? lockedBy = null,
        DateTimeOffset? lockedUntil = null)
    {
        var requestId = Guid.CreateVersion7();

        return _harness.SeedRetryScheduledStepAsync(
            RetryScannerHarness.AlphaStrategy,
            nameof(RetryScannerHarness.AlphaStep),
            typeof(RetryScannerHarness.AlphaStep).AssemblyQualifiedName!,
            JsonSerializer.SerializeToDocument(new RetryScannerHarness.AlphaStep(requestId)),
            dueIn: dueIn,
            lockedBy: lockedBy,
            lockedUntil: lockedUntil,
            id: requestId);
    }
}
