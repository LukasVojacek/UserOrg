namespace Core.Domain.Primitives;

/// <summary>
/// Unit of Work interface.
/// Provides typed repository access and persists all aggregate changes.
/// Collects domain events from tracked aggregates, persists changes via EF,
/// then dispatches events to the domain event channel.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Returns a repository for the given aggregate type.
    /// </summary>
    IRepository<TAggregate, TKey> GetRepository<TAggregate, TKey>() where TAggregate : AggregateRoot;

    /// <summary>
    /// Persists all pending changes and dispatches raised domain events.
    /// Events are dispatched only after a successful save.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}