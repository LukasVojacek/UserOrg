namespace Core.Domain.Primitives;

/// <summary>
/// Non-generic dispatcher contract — allows callers to invoke HandleAsync without knowing TEvent at compile time.
/// </summary>
public interface IDomainEventHandler
{
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handler for a specific domain event type.
/// Multiple handlers can be registered for the same event type.
/// </summary>
/// <typeparam name="TEvent">The domain event type to handle.</typeparam>
public interface IDomainEventHandler<in TEvent> : IDomainEventHandler where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);

    Task IDomainEventHandler.HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        HandleAsync((TEvent)domainEvent, cancellationToken);
}