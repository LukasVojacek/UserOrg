using Core.DataAccess.ResourceRequests;
using Core.Features.ResourceRequest.Flow;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.Features.ResourceRequest.Expiry;

/// <summary>
/// Background sweeper that reaps resource-assignment requests parked past their configured expiry (DEC-7) in
/// either gate. Reuses the existing cancel signal path (DER-2) with an <c>Expire</c> decision, so
/// <c>InvalidateLinksHandler</c> records <c>Expired</c> instead of <c>Cancelled</c> — no second termination path.
/// A row the engine cannot resume (never started, or unknown to it) is settled to <c>Expired</c> straight on the
/// projection, as <c>CancelResourceRequestEndpoint</c> does for cancel, so it stops occupying the single-active slot;
/// one whose workflow request the engine reports as <c>Failed</c> is settled to <c>Failed</c> the same way (DEC-2).
/// Poll-loop shape copied from <c>OutboxDispatcherService</c> / <c>RetryScheduledScanner</c> (DER-3); resolves its
/// scoped dependencies per poll like <c>RetryScheduledScanner</c> since both <see cref="IProjectResourceRequestQuery"/>
/// and <see cref="IWorkflowClient"/> are scoped.
/// </summary>
internal sealed class ResourceRequestExpirySweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<ResourceRequestExpiryOptions> options,
    TimeProvider timeProvider,
    ILogger<ResourceRequestExpirySweeper> logger) : BackgroundService
{
    // Expiry is measured in days (DurationInDays, default 30), so an hourly poll observes the cutoff two orders
    // of magnitude finer than it moves — a request terminates within an hour of falling due, at negligible query
    // cost. Deliberately not configurable: the operational knob is DurationInDays, not the poll cadence.
    private static readonly TimeSpan PollDelay = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Resource-request expiry sweeper started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A transient DB / poll error must not kill the loop — log and try again next poll.
                logger.LogError(ex, "Resource-request expiry sweep failed; retrying after the poll delay.");
            }

            try
            {
                await Task.Delay(PollDelay, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Resource-request expiry sweeper stopped.");
    }

    /// <summary>Internal (not private) so characterization tests can drive a single sweep deterministically.</summary>
    internal async Task SweepOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IProjectResourceRequestQuery>();
        var workflow = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
        var store = scope.ServiceProvider.GetRequiredService<IResourceRequestProjectionStore>();

        var cutoff = timeProvider.GetUtcNow().AddDays(-options.Value.DurationInDays);
        var dueForExpiry = await query.FindDueForExpiryAsync(cutoff, ct);

        foreach (var request in dueForExpiry)
        {
            await ExpireOneAsync(workflow, store, request, ct);
        }
    }

    private async Task ExpireOneAsync(
        IWorkflowClient workflow,
        IResourceRequestProjectionStore store,
        ResourceRequestDetail request,
        CancellationToken ct)
    {
        try
        {
            // Orphan row: inserted, but the workflow never started (no distributed transaction — the process can
            // die between the insert and StartAsync). There is nothing to resume, and every later sweep would
            // no-op on it while it wedges the single-active guard forever, so settle it here.
            // ponytail: CancelResourceRequestEndpoint carries the same five-line settle with Cancelled instead of
            // Expired. Two occurrences in two features; a shared helper would be a cross-file abstraction B04 may
            // invalidate when it rewires the second gate — kept duplicated deliberately.
            if (request.WorkflowRequestId == Guid.Empty)
            {
                await SettleAsync(store, request.Slug, ResourceRequestState.Expired, ct);
                logger.LogWarning(
                    "Expired orphaned resource request {Slug}: it has no workflow to resume.", request.Slug);
                return;
            }

            // Which gate the flow is parked on decides the signal type; the query yields only these two
            // non-terminal states, so a row in any other state is already terminal and needs no sweep.
            var state = ResourceRequestState.From(request.State);
            ResumeResult resume;
            if (state == ResourceRequestState.PendingPiApproval)
            {
                resume = await workflow.ResumeAsync(
                    request.WorkflowRequestId, new PiDecision(PiDecisionKind.Expire), ct);
            }
            else if (state == ResourceRequestState.AwaitingResourceCreation)
            {
                resume = await workflow.ResumeAsync(
                    request.WorkflowRequestId, new ResourceAvailabilitySignal(ResourceAvailabilityKind.Expire), ct);
            }
            else
            {
                return;
            }

            await HandleResumeOutcomeAsync(workflow, store, request, resume, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // One bad row must not stop the sweep; it stays non-terminal and is naturally re-picked next poll.
            logger.LogError(ex, "Failed to expire resource request {Slug}; will retry next sweep.", request.Slug);
        }
    }

    private async Task HandleResumeOutcomeAsync(
        IWorkflowClient workflow,
        IResourceRequestProjectionStore store,
        ResourceRequestDetail request,
        ResumeResult resume,
        CancellationToken ct)
    {
        if (resume.Outcome == ResumeOutcome.Resumed)
            return;

        // The engine has no such request, so the row is orphaned just like a never-started one.
        if (resume.Outcome == ResumeOutcome.RequestNotFound)
        {
            await SettleOrphanAsync(store, request.Slug, ct);
            return;
        }

        if (resume.Outcome == ResumeOutcome.NotAwaiting)
        {
            await HandleNotAwaitingAsync(workflow, store, request, ct);
            return;
        }

        // SignalTypeMismatch means the flow is parked on a gate this sweeper does not model. Not actionable here,
        // and it may not wedge the loop — the next sweep re-picks the row if it is still due.
        logger.LogWarning(
            "Could not expire resource request {Slug}: resume returned {Outcome}.", request.Slug, resume.Outcome);
    }

    /// <summary>
    /// <c>NotAwaiting</c> conflates a permanently dead request with a live one — it is returned both when the
    /// aggregate is simply not parked (<c>WorkflowClient</c>'s status check) and when it *is* parked but the named
    /// gate cannot be resolved. Only the engine's own status tells the two apart, so ask for it here — and only
    /// here (DER-4): the cost is one round trip per genuinely stuck row, never one per swept row.
    /// </summary>
    private async Task HandleNotAwaitingAsync(
        IWorkflowClient workflow,
        IResourceRequestProjectionStore store,
        ResourceRequestDetail request,
        CancellationToken ct)
    {
        var slug = request.Slug;
        var status = await workflow.GetStatusAsync(request.WorkflowRequestId, ct);

        // Three arms below deliberately leave the row untouched for three different reasons, and the log level
        // is the verdict: debug = a benign race that resolved itself, warning = stuck but expected and not a
        // defect, error = the projection and the engine disagree and a human must reconcile them. Folding them
        // into one "leave it" arm would erase that distinction.
        // No default arm on purpose: every RequestStatus member plus null has an explicit verdict here, so a
        // status added upstream must be given one rather than silently inherit "leave the row alone".
        switch (status)
        {
            case null:
            // Same condition as RequestNotFound, so the same remedy (DER-3).
            await SettleOrphanAsync(store, slug, ct);
            return;

            case RequestStatus.Failed:
            await SettleAsync(store, slug, ResourceRequestState.Failed, ct);
            logger.LogWarning(
                "Settled resource request {Slug} as Failed: the engine reports the request failed. Nothing resumes it on its own; only an explicit operator retry could, and no such path exists today.",
                slug);
            return;

            case RequestStatus.Completed:
            await DiagnoseCompletedRequestAsync(store, slug, ct);
            return;

            case RequestStatus.AwaitingSignal:
            // The request is parked, yet the engine could not route the signal — the gate the projection
            // implies does not exist on the flow. But this same observation also follows a benign race: the
            // request was still InProgress when ResumeAsync ran (correctly NotAwaiting) and parked at a gate
            // before this GetStatusAsync round trip. Nothing here tells the two apart, so this is not yet
            // "something is wrong" — warning, not error; the row stays for a human only if it recurs (DER-6).
            logger.LogWarning(
                "Resource request {Slug} is awaiting a signal the workflow engine cannot route to a gate; this may be the projection and the flow diverging, or a benign race with a request that just parked. The row will not expire on its own; it is re-checked next sweep.", slug);
            return;

            case RequestStatus.Accepted or RequestStatus.InProgress:
            // Alive by the engine's own account, but a row this old should long since have parked — settling
            // it would terminate work the engine believes is running, so it stays wedged, knowingly (DER-5).
            logger.LogWarning(
                "Resource request {Slug} has a stalled workflow request ({Status}) unchanged since {UpdatedAt:u}: it is neither parked at a gate nor finished, so the sweeper cannot expire it and it keeps the single-active slot for its (project, provider, resource) until the engine moves or an operator intervenes.", slug, status, request.UpdatedAt);
            return;
        }
    }

    /// <summary>
    /// The sweep resumes rows from an <c>AsNoTracking</c> snapshot, so a PI decision landing between snapshot and
    /// resume completes the flow legitimately while the snapshot still reads non-terminal. A fresh read separates
    /// that benign race from the flow's terminal step never having written the projection (DER-2).
    /// </summary>
    private async Task DiagnoseCompletedRequestAsync(
        IResourceRequestProjectionStore store, string slug, CancellationToken ct)
    {
        var current = await store.GetBySlugAsync(slug, ct);
        if (current is null || current.State.IsTerminal)
        {
            logger.LogDebug(
                "Resource request {Slug} completed while the sweep was in flight; nothing left to expire.", slug);
            return;
        }

        logger.LogError(
            "Resource request {Slug} is Completed in the workflow engine but its projection row is still {State}; the flow's terminal step never wrote the projection.", slug, current.State);
    }

    private async Task SettleOrphanAsync(IResourceRequestProjectionStore store, string slug, CancellationToken ct)
    {
        await SettleAsync(store, slug, ResourceRequestState.Expired, ct);
        logger.LogWarning(
            "Expired orphaned resource request {Slug}: the workflow engine does not know it.", slug);
    }

    private static Task SettleAsync(
        IResourceRequestProjectionStore store, string slug, ResourceRequestState state, CancellationToken ct) =>
        store.UpdateBySlugAsync(slug, row =>
        {
            // The projection has no concurrency token, so this delegate — applied to the tracked row immediately
            // before SaveChanges — is the last read before the write. A row the flow settled itself in the
            // meantime must survive: clobbering a legitimate Assigned with a sweeper verdict is worse than
            // leaving the row alone, and a terminal row already holds no single-active slot.
            // This closes the application-level window only. The residual gap is the read-committed one between
            // the store's SELECT and the UPDATE inside the same SaveChanges; only a rowversion on the projection
            // would close that, which is a schema change outside this guard.
            if (row.State.IsTerminal)
            {
                return;
            }

            row.State = state;
            row.TokenHash = null;
            row.TokenExpiresAt = null;
        }, ct);
}