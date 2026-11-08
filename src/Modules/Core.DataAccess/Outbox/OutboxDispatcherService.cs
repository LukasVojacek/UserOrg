using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Background dispatcher that delivers outbox rows to completion (ADR-0040). Polls undispatched
/// <c>outbox.Outbox</c> rows, delivers each through the <see cref="IOutboxDispatchTarget"/> seam (in-process
/// handlers today; a broker tomorrow), marks the row dispatched on success, and retries with backoff
/// (<see cref="OutboxRetryPolicy"/>) — then dead-letters via a critical log — on failure.
/// </summary>
internal sealed class OutboxDispatcherService(
    IDbContextFactory<OutboxDbContext> contextFactory,
    IOutboxDispatchTarget target,
    ILogger<OutboxDispatcherService> logger) : BackgroundService
{
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(10);
    private const int BatchSize = 50;
    private const int MaxDegreeOfParallelism = 4;

    /// <summary>Max length of a stored <see cref="OutboxMessage.Error"/> (keeps the column bounded).</summary>
    private const int MaxErrorLength = 2000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox dispatcher started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient DB / poll error must not kill the loop — log and try again next poll.
                logger.LogError(ex, "Outbox poll failed; retrying after the poll delay.");
            }

            try
            {
                await Task.Delay(PollDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Outbox dispatcher stopped.");
    }

    /// <summary>Internal (not private) so characterization tests can drive a single poll deterministically.</summary>
    internal async Task PollOnceAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        List<OutboxMessage> batch;
        await using (var ctx = await contextFactory.CreateDbContextAsync(ct))
        {
            // Single-instance assumption — no row-claim locking (ADR-0054 D4, deferred by design). With one
            // API instance, two pollers cannot race a row. HORIZONTAL-SCALE UPGRADE SEAM: to run multiple
            // instances safely, claim rows atomically here so two instances never deliver the same row twice —
            // e.g. SQL Server `SELECT ... WITH (UPDLOCK, READPAST)` (skip rows another instance already locked),
            // or an `UPDATE outbox.Outbox SET ClaimedAt = @now OUTPUT inserted.* WHERE ...` claim-and-fetch.
            // This is the one place that change lands.
            batch = await ctx.OutboxMessages
                .AsNoTracking()
                .Where(m => m.DispatchedAt == null && m.Attempts < OutboxRetryPolicy.MaxAttempts)
                .OrderBy(m => m.OccurredAt)
                .Take(BatchSize)
                .ToListAsync(ct);
        }

        // Backoff is applied in memory (keeps the SQL simple + provider-agnostic): keep only rows whose
        // backoff window has elapsed.
        var due = batch
            .Where(m => OutboxRetryPolicy.IsDue(m.Attempts, m.LastAttemptAt, now))
            .ToList();

        if (due.Count == 0)
            return;

        // Bounded parallelism mirrors the old ChannelWorker (4 consumers). Each row is independent.
        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = MaxDegreeOfParallelism, CancellationToken = ct },
            async (message, token) => await ProcessRowAsync(message, token));
    }

    private async Task ProcessRowAsync(OutboxMessage message, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Span per row (ADR-0054 D1): links the dispatch to the downstream HEAppE call in the trace view.
        using var activity = OutboxTelemetry.ActivitySource.StartActivity(OutboxTelemetry.ActivityDispatchRow);
        activity?.SetTag(OutboxTelemetry.TagMessageId, message.Id);
        activity?.SetTag(OutboxTelemetry.TagMessageType, message.Type);
        activity?.SetTag(OutboxTelemetry.TagAttempts, message.Attempts);

        var typeTag = new TagList { { OutboxTelemetry.TagMessageType, message.Type } };
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await target.DispatchAsync(message, ct);
            message.DispatchedAt = now;

            OutboxTelemetry.Dispatched.Add(1, typeTag);
            OutboxTelemetry.DispatchDuration.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds, typeTag);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            message.Attempts++;
            message.LastAttemptAt = now;
            message.Error = Truncate(ex.Message, MaxErrorLength);

            OutboxTelemetry.Failed.Add(1, typeTag);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            if (message.Attempts >= OutboxRetryPolicy.MaxAttempts)
            {
                OutboxTelemetry.DeadLettered.Add(1, typeTag);

                // Dead-letter / stuck-row signal for the operator (ADR-0028, ADR-0040).
                logger.LogCritical(
                    ex,
                    "Outbox row {OutboxId} of type {OutboxType} dead-lettered after {Attempts} attempts.",
                    message.Id, message.Type, message.Attempts);
            }
            else
            {
                logger.LogWarning(
                    ex,
                    "Outbox row {OutboxId} of type {OutboxType} failed (attempt {Attempts}); will retry with backoff.",
                    message.Id, message.Type, message.Attempts);
            }

            // Do NOT rethrow — one bad row must not stop the batch or the loop.
        }

        await PersistRowAsync(message, ct);
    }

    private async Task PersistRowAsync(OutboxMessage message, CancellationToken ct)
    {
        try
        {
            // Fresh context per row: the batch was read AsNoTracking, so re-attach and update just this row.
            await using var ctx = await contextFactory.CreateDbContextAsync(ct);
            ctx.OutboxMessages.Update(message);
            await ctx.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Persisting the outcome failed (transient DB error). The row stays undispatched / unincremented
            // and is naturally re-picked on the next poll — log and continue.
            logger.LogError(ex, "Failed to persist outbox row {OutboxId} state; it will be re-picked next poll.", message.Id);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}