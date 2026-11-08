namespace Core.Domain.Primitives;

/// <summary>
/// Marker interface for all domain events.
/// Implementing this interface means the event is processed in-process only.
/// </summary>
public interface IDomainEvent;