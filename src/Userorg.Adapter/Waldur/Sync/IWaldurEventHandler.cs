using Lukdrasil.Error;
using Lukdrasil.Result;

using Waldur.Contracts.Events;

namespace Userorg.Adapter.Waldur.Sync;

/// <summary>
/// Consumer-side handler for a single concrete Waldur event type (ADR-0057). One handler per
/// event type, shared by all instances; EFP/Puhuri differences are resolved by payload content.
/// A successful <see cref="Result{TError}"/> from <see cref="HandleAsync"/> is the ack; a
/// <see cref="Result{TError}"/> failure (expected dependency-missing) means "not acked" and the
/// event replays next iteration (ADR-0065). Unexpected faults (programming/infrastructure) still
/// throw and are caught by the worker's backstop — a handler must never skip-and-ack.
/// </summary>
/// <typeparam name="TEvent">The concrete event this handler consumes.</typeparam>
internal interface IWaldurEventHandler<in TEvent>
    where TEvent : WaldurIntegrationEvent
{
    /// <summary>
    /// Applies the event to the store. Returns <see cref="Result{TError}"/> failure for an expected
    /// dependency-missing failure (not acked, replays); throws only for unexpected faults.
    /// </summary>
    Task<Result<Error>> HandleAsync(TEvent evt, CancellationToken ct);
}