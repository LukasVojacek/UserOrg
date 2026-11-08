using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Ports;

/// <summary>
/// Driven port — abstraction over the persistence store for <see cref="RequestAggregate"/>.
/// Implemented by the consuming infrastructure project (e.g. WorkflowEngine.Infrastructure with EF Core).
/// </summary>
public interface IRequestRepository
{
    /// <summary>
    /// Persists a new aggregate as a single atomic unit.
    /// Sets <see cref="RequestAggregate.RowVersion"/> on success.
    /// </summary>
    Task AddAsync(RequestAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Loads an aggregate by its primary key, including all step records.
    /// Returns <c>null</c> when the request does not exist.
    /// </summary>
    Task<RequestAggregate?> FindByIdAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Loads an aggregate by its idempotency key.
    /// Returns <c>null</c> when no matching record is found (key unused or expired).
    /// </summary>
    Task<RequestAggregate?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Loads an aggregate by its provider name and resource name.
    /// </summary>
    /// <param name="providerName"></param>
    /// <param name="resourceName"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<RequestAggregate?> FindByProviderResourceNameAsync(string providerName, string resourceName, CancellationToken ct = default);

    /// <summary>
    /// Persists state changes to an existing aggregate using optimistic concurrency (row-version check).
    /// Throws <see cref="DbUpdateConcurrencyException"/> (or equivalent) on concurrent modification.
    /// Updates <see cref="RequestAggregate.RowVersion"/> on success.
    /// </summary>
    Task SaveAsync(RequestAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Returns a paged list of request summaries matching the supplied filter.
    /// </summary>
    Task<Contracts.Models.PagedResult<Contracts.Models.RequestSummaryItem>> ListAsync(
        Contracts.Models.RequestFilter filter,
        CancellationToken ct = default);

    /// <summary>
    /// Returns aggregated statistics for requests matching the supplied filter.
    /// </summary>
    Task<Contracts.Models.RequestStats> GetStatsAsync(
        Contracts.Models.StatsFilter filter,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all aggregates that are currently in <c>InProgress</c> status
    /// and have at least one step with an expired lease.
    /// Used by startup-recovery to re-enqueue stalled work.
    /// </summary>
    Task<IReadOnlyList<RequestAggregate>> FindStalledAsync(DateTimeOffset leaseExpiredBefore, CancellationToken ct = default);

    /// <summary>
    /// Returns aggregates whose durable row has no live step to drive them forward, even though nothing has
    /// claimed a lease on any step — the two classes <see cref="FindStalledAsync"/> cannot see:
    /// <list type="bullet">
    ///   <item><c>Accepted</c> with no step row at all (never dequeued).</item>
    ///   <item><c>InProgress</c> with no <c>Running</c> step and no <c>RetryScheduled</c> step (the shape a
    ///   completed-step-then-lost-enqueue, or a gate resume, leaves behind).</item>
    /// </list>
    /// <c>RetryScheduled</c> is excluded on purpose: it is <c>InProgress</c> with no <c>Running</c> step too,
    /// but it belongs to <see cref="FindDueRetriesAsync"/> and re-enqueueing it here would skip its back-off.
    /// Used by startup-recovery to re-enqueue work an in-memory queue lost.
    /// </summary>
    Task<IReadOnlyList<RequestAggregate>> FindStrandedAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns all step records that are in <c>RetryScheduled</c> status
    /// and whose <c>NextRetryAt</c> is at or before <paramref name="now"/>.
    /// Includes the parent aggregate for each step.
    /// </summary>
    Task<IReadOnlyList<(RequestAggregate Aggregate, Domain.Request.RequestStep Step)>> FindDueRetriesAsync(
        DateTimeOffset now,
        CancellationToken ct = default);
    /// <summary>
    /// Attempts to acquire an exclusive lease on the named step for this worker instance.
    /// Returns <c>true</c> when the lease was granted (no other worker holds a live lease).
    /// The lease expiry is determined by the module options (<c>LeaseDuration</c>).
    /// </summary>
    Task<bool> TryAcquireStepLeaseAsync(Guid requestId, string stateName, string workerId, TimeSpan duration, CancellationToken ct = default);

    /// <summary>
    /// Releases the lease on the named step, making it available for the next worker or scanner.
    /// </summary>
    Task ReleaseStepLeaseAsync(Guid requestId, string stateName, CancellationToken ct = default);
}