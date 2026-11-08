namespace Core.Domain.Primitives;

/// <summary>
/// Marker interface for domain events that should also be propagated to external systems
/// (e.g., webhooks, message brokers).
/// Prepared for future implementation — currently treated the same as IDomainEvent.
/// </summary>
public interface IExternalEvent : IDomainEvent;