namespace Core.DataAccess.Outbox;

/// <summary>
/// The delivery seam for the outbox dispatcher (ADR-0040). "Deliver this message; throw on failure."
/// <para>
/// This is the <b>swap point</b> that keeps the poll loop (<see cref="OutboxDispatcherService"/>) decoupled
/// from <i>how</i> a message is actually delivered. The in-process target
/// (<see cref="InProcessOutboxDispatchTarget"/>) deserializes the row and runs the matching domain-event
/// handlers in-process. A future broker target would instead publish to RabbitMQ here — an adapter swap,
/// with no change to the loop, the retry/backoff policy, or the dead-lettering.
/// </para>
/// </summary>
internal interface IOutboxDispatchTarget
{
    /// <summary>
    /// Delivers a single outbox message to completion. Must throw on any failure so the dispatcher can
    /// increment <see cref="OutboxMessage.Attempts"/> and retry with backoff (then dead-letter).
    /// </summary>
    /// <param name="message">The undispatched outbox row.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DispatchAsync(OutboxMessage message, CancellationToken ct);
}