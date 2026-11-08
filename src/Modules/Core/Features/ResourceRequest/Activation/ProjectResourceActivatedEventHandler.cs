using Core.Features.ResourceRequest.Flow;

using Microsoft.Extensions.Logging;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

namespace Core.Features.ResourceRequest.Activation;

/// <summary>
/// Outbox-dispatched handler for <see cref="ProjectResourceActivatedEvent"/> (DEC-4). When the awaited resource
/// is created, every assignment request parked on the ResourceAvailability gate for that resource key must
/// resume. Implements durable retry semantics (ADR-0028): only throws (which triggers outbox retry) when
/// resume outcome is not Resumed (transient races and unexpected engine states); already-settled rows that have
/// moved past AwaitingResourceCreation are silently skipped on idempotent replays.
/// </summary>
/// <remarks>
/// Per-row outcome handling, so one unresumable request never wedges its siblings for the same key:
/// <list type="bullet">
/// <item><see cref="ResumeOutcome.Resumed"/> — success.</item>
/// <item><see cref="ResumeOutcome.NotAwaiting"/> — TRANSIENT. Ordering hazard (from
/// <c>NotifyProviderResourceNeededHandler</c>): the engine only parks the <c>ResourceAvailability</c> gate
/// AFTER that handler returns, yet the projection row already advertises <c>AwaitingResourceCreation</c>. A
/// resume racing that residual window sees the row as awaiting while the engine is not yet parked, so the whole
/// event must be retried — the surviving siblings are resumed first (below), then the handler throws once at the
/// end to trigger a durable retry for the not-yet-parked one(s).</item>
/// <item><see cref="ResumeOutcome.SignalTypeMismatch"/> / <see cref="ResumeOutcome.RequestNotFound"/> —
/// PERMANENT for that row (a corrupt/misrouted projection row). Logged and skipped so it does not block the
/// batch; retrying would never help it.</item>
/// </list>
/// Idempotency needs no special handling: a row that already resumed has moved past
/// <c>AwaitingResourceCreation</c>, so <see cref="IProjectResourceRequestQuery.FindAwaitingByResourceKeyAsync"/>
/// no longer returns it and it is silently skipped.
/// </remarks>
internal sealed class ProjectResourceActivatedEventHandler(
    IProjectResourceRequestQuery requests,
    IWorkflowClient workflow,
    ILogger<ProjectResourceActivatedEventHandler> logger) : IDomainEventHandler<ProjectResourceActivatedEvent>
{
    public async Task HandleAsync(ProjectResourceActivatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var key = domainEvent.Resource;
        var awaiting = await requests.FindAwaitingByResourceKeyAsync(key.Provider.Value, key.Name.Value, cancellationToken);

        var transient = false;
        foreach (var row in awaiting)
        {
            var result = await workflow.ResumeAsync(
                row.WorkflowRequestId,
                new ResourceAvailabilitySignal(ResourceAvailabilityKind.Available),
                cancellationToken);

            switch (result.Outcome)
            {
                case ResumeOutcome.Resumed:
                break;

                case ResumeOutcome.NotAwaiting:
                // The engine has not parked this row's gate yet — retry the event, but keep resuming its
                // siblings in this pass so a single racing row does not hold them all back.
                transient = true;
                break;

                default:
                // SignalTypeMismatch / RequestNotFound — permanent for this row; a retry never fixes it, so
                // skip it instead of failing the whole batch (its siblings still get resumed).
                logger.LogWarning(
                    "Skipping resume of request {RequestId} for resource {Provider}/{Resource}: {Outcome}.",
                    row.WorkflowRequestId, key.Provider.Value, key.Name.Value, result.Outcome);
                break;
            }
        }

        if (transient)
        {
            throw new InvalidOperationException(
                $"One or more requests for resource {key.Provider.Value}/{key.Name.Value} were not yet awaiting the availability signal; retrying.");
        }
    }
}