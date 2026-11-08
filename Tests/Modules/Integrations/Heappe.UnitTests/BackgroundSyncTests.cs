using Heappe.Contracts.Entries.Sync;
using Heappe.Features.BackgroundSync;
using Heappe.Features.CommandTemplateSynchronization;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace Heappe.UnitTests;

public sealed class InProgressTrackerTests
{
    private readonly InProgressTracker _tracker;

    public InProgressTrackerTests()
    {
        _tracker = new InProgressTracker(TimeSpan.FromMinutes(30));
    }

    [Fact(DisplayName = "TryMarkInProgress should mark new item as in progress")]
    public void TryMarkInProgress_Should_ReturnTrue_When_ItemNew()
    {
        var key = "project:resource";
        var startTime = DateTime.UtcNow;

        var result = _tracker.TryMarkInProgress(key, startTime);

        Assert.True(result);
        Assert.True(_tracker.IsInProgress(key));
    }

    [Fact(DisplayName = "TryMarkInProgress should reject duplicate")]
    public void TryMarkInProgress_Should_ReturnFalse_When_ItemAlreadyInProgress()
    {
        var key = "project:resource";
        var startTime = DateTime.UtcNow;

        _tracker.TryMarkInProgress(key, startTime);
        var result = _tracker.TryMarkInProgress(key, startTime.AddSeconds(5));

        Assert.False(result);
    }

    [Fact(DisplayName = "HasTimedOut should return false for recent items")]
    public void HasTimedOut_Should_ReturnFalse_When_ItemRecent()
    {
        var key = "project:resource";
        var startTime = DateTime.UtcNow;

        _tracker.TryMarkInProgress(key, startTime);
        var result = _tracker.HasTimedOut(key);

        Assert.False(result);
    }

    [Fact(DisplayName = "HasTimedOut should return true for timed-out items")]
    public void HasTimedOut_Should_ReturnTrue_When_ItemExceedsTimeout()
    {
        var key = "project:resource";
        var pastTime = DateTime.UtcNow.AddMinutes(-31);

        _tracker.TryMarkInProgress(key, pastTime);
        var result = _tracker.HasTimedOut(key);

        Assert.True(result);
    }

    [Fact(DisplayName = "MarkCompleted should remove item")]
    public void MarkCompleted_Should_RemoveItem()
    {
        var key = "project:resource";
        var startTime = DateTime.UtcNow;

        _tracker.TryMarkInProgress(key, startTime);
        _tracker.MarkCompleted(key);

        Assert.False(_tracker.IsInProgress(key));
    }

    [Fact(DisplayName = "UpdateStartTime should reset timeout")]
    public void UpdateStartTime_Should_ResetTimeout()
    {
        var key = "project:resource";
        var oldTime = DateTime.UtcNow.AddMinutes(-31);

        _tracker.TryMarkInProgress(key, oldTime);
        Assert.True(_tracker.HasTimedOut(key));

        _tracker.UpdateStartTime(key, DateTime.UtcNow);
        Assert.False(_tracker.HasTimedOut(key));
    }
}

public sealed class WorkItemProcessorTests
{
    private readonly Mock<IProjectCommandTemplateSyncService> _syncServiceMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly InProgressTracker _tracker;
    private readonly WorkItemProcessor _processor;

    public WorkItemProcessorTests()
    {
        _syncServiceMock = new Mock<IProjectCommandTemplateSyncService>();
        _loggerMock = new Mock<ILogger>();
        _tracker = new InProgressTracker(TimeSpan.FromMinutes(30));
        _processor = new WorkItemProcessor(_syncServiceMock.Object, _tracker, _loggerMock.Object);
    }

    // B03: SyncCommandTemplatesForResource now returns ValidationResult<CommandTemplateSyncResult>.
    // The WorkItemProcessor (B05-stub) still ignores the result, so any all-succeeded result keeps
    // today's behavior for these baseline tests. (B05 will overwrite this file.)
    private static CommandTemplateSyncResult AllSucceededResult() =>
        new(
        [
            new LocationSyncResultEntry("loc", "AGG", new Uri("http://heappe.test"), IsSuccess: true, Errors: [])
        ]);

    [Fact(DisplayName = "ProcessAsync should sync when item is not in progress")]
    public async Task ProcessAsync_Should_ExecuteSync_When_ItemNotInProgress()
    {
        var item = new WorkItem("TestProject", "TestResource");
        using var cts = new CancellationTokenSource();

        _syncServiceMock
            .Setup(x => x.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, cts.Token))
            .ReturnsAsync(Lukdrasil.Result.ValidationResult<CommandTemplateSyncResult>.Success(AllSucceededResult()))
            .Verifiable();

        await _processor.ProcessAsync(item, consumerId: 1, cts.Token);

        _syncServiceMock.Verify();
        Assert.False(_tracker.IsInProgress(item.Key));
    }

    [Fact(DisplayName = "ProcessAsync should skip duplicate items")]
    public async Task ProcessAsync_Should_SkipDuplicate_When_ItemAlreadyInProgress()
    {
        var item = new WorkItem("TestProject", "TestResource");
        _tracker.TryMarkInProgress(item.Key, DateTime.UtcNow);
        using var cts = new CancellationTokenSource();

        await _processor.ProcessAsync(item, consumerId: 1, cts.Token);

        _syncServiceMock.Verify(
            x => x.SyncCommandTemplatesForResource(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "ProcessAsync should retry after timeout")]
    public async Task ProcessAsync_Should_RetryAfterTimeout_When_ItemExceedsTimeout()
    {
        var item = new WorkItem("TestProject", "TestResource");
        var pastTime = DateTime.UtcNow.AddMinutes(-31);

        _tracker.TryMarkInProgress(item.Key, pastTime);
        using var cts = new CancellationTokenSource();

        _syncServiceMock
            .Setup(x => x.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, cts.Token))
            .ReturnsAsync(Lukdrasil.Result.ValidationResult<CommandTemplateSyncResult>.Success(AllSucceededResult()))
            .Verifiable();

        await _processor.ProcessAsync(item, consumerId: 1, cts.Token);

        _syncServiceMock.Verify();
    }

    [Fact(DisplayName = "ProcessAsync should handle sync exceptions")]
    public async Task ProcessAsync_Should_HandleException_When_SyncFails()
    {
        var item = new WorkItem("TestProject", "TestResource");
        using var cts = new CancellationTokenSource();

        _syncServiceMock
            .Setup(x => x.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, cts.Token))
            .ThrowsAsync(new InvalidOperationException("Sync failed"));

        await _processor.ProcessAsync(item, consumerId: 1, cts.Token);

        Assert.False(_tracker.IsInProgress(item.Key));
    }

    [Fact(DisplayName = "ProcessAsync should handle cancellation")]
    public async Task ProcessAsync_Should_HandleCancellation_When_TokenCancelled()
    {
        var item = new WorkItem("TestProject", "TestResource");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _syncServiceMock
            .Setup(x => x.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, cts.Token))
            .ThrowsAsync(new OperationCanceledException());

        await _processor.ProcessAsync(item, consumerId: 1, cts.Token);

        Assert.False(_tracker.IsInProgress(item.Key));
    }

    private static System.Diagnostics.ActivityListener CaptureSyncActivity(out Func<System.Diagnostics.Activity?> getCaptured)
    {
        System.Diagnostics.Activity? captured = null;
        var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == Heappe.Features.BackgroundSync.HeappeSyncTelemetry.ActivitySourceName,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == Heappe.Features.BackgroundSync.HeappeSyncTelemetry.ActivitySyncResource)
                {
                    captured = activity;
                }
            }
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        getCaptured = () => captured;
        return listener;
    }

    private static object? TagValue(System.Diagnostics.Activity activity, string tagKey) =>
        activity.TagObjects.FirstOrDefault(t => t.Key == tagKey).Value;

    private void VerifyLog(LogLevel level, Times times, string? mustContain = null) =>
        _loggerMock.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => mustContain == null || state.ToString()!.Contains(mustContain)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    /// <summary>
    /// Item 9 (Phase 0 baseline, plan-ready.md) — UPDATED for B05's new semantics. A top-level
    /// Failure result now produces <c>allSucceeded == false</c>, so <c>heappe_sync.success</c>
    /// is set to <c>false</c> (no longer unconditionally <c>true</c>). Cleanup is unaffected:
    /// <c>ProcessAsync</c> still completes without throwing and <c>_tracker.MarkCompleted</c>
    /// still runs. This is one of the assertions the plan flagged as intentionally changing.
    /// </summary>
    [Fact(DisplayName = "A top-level Failure sync result now reports TagSuccess=false; ProcessAsync still completes and cleans up")]
    public async Task ProcessAsync_TopLevelFailure_ReportsSuccessFalse_AndStillCompletes()
    {
        var item = new WorkItem("TestProject", "TestResource");
        using var cts = new CancellationTokenSource();

        var error = new Lukdrasil.Error.ValidationError(
            new Lukdrasil.Error.Error("ERR001", Lukdrasil.Error.ErrorSeverity.Error, "sync failed"));

        _syncServiceMock
            .Setup(x => x.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, cts.Token))
            .ReturnsAsync(Lukdrasil.Result.ValidationResult<CommandTemplateSyncResult>.Failure(error))
            .Verifiable();

        var listener = CaptureSyncActivity(out var getCaptured);
        try
        {
            await _processor.ProcessAsync(item, consumerId: 1, cts.Token);
        }
        finally
        {
            listener.Dispose();
        }

        _syncServiceMock.Verify();
        Assert.False(_tracker.IsInProgress(item.Key), "MarkCompleted must still run on a Failure result (cleanup unaffected).");

        var syncActivity = getCaptured();
        Assert.NotNull(syncActivity);
        // Top-level failure has no per-location breakdown: not "AllSucceeded".
        Assert.Equal(false, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagSuccess));
        Assert.Equal(0, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagLocationsSucceeded));
        Assert.Equal(0, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagLocationsFailed));

        // A top-level failure must be logged as a warning, never as an info "COMPLETED" line.
        VerifyLog(LogLevel.Warning, Times.Once(), "FAILED");
        VerifyLog(LogLevel.Information, Times.Never(), "COMPLETED");
    }

    /// <summary>
    /// Phase 3 item 8 (plan-ready.md): a partial-success result (2 locations succeed, 1 fails)
    /// must log a warning naming the failed location and its error, set TagSuccess=false with
    /// the right per-location counts, still call MarkCompleted, and not throw.
    /// </summary>
    [Fact(DisplayName = "Partial-success sync result logs a warning, reports TagSuccess=false and correct location counts")]
    public async Task ProcessAsync_PartialSuccess_LogsWarning_AndReportsPerLocationCounts()
    {
        var item = new WorkItem("TestProject", "TestResource");
        using var cts = new CancellationTokenSource();

        var failedError = new Lukdrasil.Error.Error("HEAPPE_CLUSTERS", Lukdrasil.Error.ErrorSeverity.Error, "failed to list clusters");
        var result = new CommandTemplateSyncResult(
        [
            new LocationSyncResultEntry("loc-ok-1", "AGG1", new Uri("http://heappe.test"), IsSuccess: true, Errors: []),
            new LocationSyncResultEntry("loc-ok-2", "AGG2", new Uri("http://heappe.test"), IsSuccess: true, Errors: []),
            new LocationSyncResultEntry("loc-bad", "AGG3", new Uri("http://heappe.broken"), IsSuccess: false, Errors: [failedError]),
        ]);

        _syncServiceMock
            .Setup(x => x.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, cts.Token))
            .ReturnsAsync(Lukdrasil.Result.ValidationResult<CommandTemplateSyncResult>.Success(result))
            .Verifiable();

        var listener = CaptureSyncActivity(out var getCaptured);
        try
        {
            await _processor.ProcessAsync(item, consumerId: 1, cts.Token);
        }
        finally
        {
            listener.Dispose();
        }

        _syncServiceMock.Verify();

        // Warning names the failed location and its error; no "COMPLETED" info line for a partial run.
        VerifyLog(LogLevel.Warning, Times.Once(), "loc-bad");
        VerifyLog(LogLevel.Warning, Times.Once(), "failed to list clusters");
        VerifyLog(LogLevel.Information, Times.Never(), "COMPLETED");

        Assert.False(_tracker.IsInProgress(item.Key), "MarkCompleted must still run on a partial failure (cleanup unaffected).");

        var syncActivity = getCaptured();
        Assert.NotNull(syncActivity);
        Assert.Equal(false, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagSuccess));
        Assert.Equal(2, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagLocationsSucceeded));
        Assert.Equal(1, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagLocationsFailed));
    }

    /// <summary>
    /// Phase 3 item 8 (plan-ready.md), second case: all locations succeed → info-level COMPLETED
    /// (no warning) and TagSuccess=true.
    /// </summary>
    [Fact(DisplayName = "All-succeeded sync result logs COMPLETED (no warning) and reports TagSuccess=true")]
    public async Task ProcessAsync_AllSucceeded_LogsCompleted_AndReportsSuccessTrue()
    {
        var item = new WorkItem("TestProject", "TestResource");
        using var cts = new CancellationTokenSource();

        var result = new CommandTemplateSyncResult(
        [
            new LocationSyncResultEntry("loc-1", "AGG1", new Uri("http://heappe.test"), IsSuccess: true, Errors: []),
            new LocationSyncResultEntry("loc-2", "AGG2", new Uri("http://heappe.test"), IsSuccess: true, Errors: []),
        ]);

        _syncServiceMock
            .Setup(x => x.SyncCommandTemplatesForResource(item.ProjectShortName, item.ResourceName, cts.Token))
            .ReturnsAsync(Lukdrasil.Result.ValidationResult<CommandTemplateSyncResult>.Success(result))
            .Verifiable();

        var listener = CaptureSyncActivity(out var getCaptured);
        try
        {
            await _processor.ProcessAsync(item, consumerId: 1, cts.Token);
        }
        finally
        {
            listener.Dispose();
        }

        _syncServiceMock.Verify();

        VerifyLog(LogLevel.Information, Times.Once(), "COMPLETED");
        VerifyLog(LogLevel.Warning, Times.Never());

        var syncActivity = getCaptured();
        Assert.NotNull(syncActivity);
        Assert.Equal(true, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagSuccess));
        Assert.Equal(2, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagLocationsSucceeded));
        Assert.Equal(0, TagValue(syncActivity!, Heappe.Features.BackgroundSync.HeappeSyncTelemetry.TagLocationsFailed));
    }
}