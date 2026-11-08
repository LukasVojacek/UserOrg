namespace Core.Domain.Primitives;

/// <summary>
/// Base class for all aggregate roots.
/// Maintains a list of domain events raised during the aggregate's lifetime.
/// Events are dispatched externally (via IUnitOfWork) after a successful SaveChanges.
/// </summary>
public abstract class AggregateRoot : BaseAuditableEntity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Domain events raised since the last save.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Registers a domain event to be dispatched after the next successful SaveChanges.
    /// </summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Clears all domain events. Called by IUnitOfWork after events are dispatched.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}