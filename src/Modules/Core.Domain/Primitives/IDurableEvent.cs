namespace Core.Domain.Primitives;

/// <summary>
/// Marker for domain events that require guaranteed delivery (ADR-0028, ADR-0040).
/// Events implementing this interface are persisted to the transactional outbox in the same
/// transaction as the aggregate change, rather than being dispatched on the at-most-once
/// in-memory <c>DomainEventChannel</c>. Best-effort events (cache eviction, etc.) stay on the
/// in-memory channel and must NOT implement this.
/// </summary>
public interface IDurableEvent : IDomainEvent;