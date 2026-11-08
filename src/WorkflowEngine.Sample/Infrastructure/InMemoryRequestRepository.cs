using System.Collections.Concurrent;
using System.Collections.Immutable;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Sample.Infrastructure;

/// <summary>
/// Volatile in-memory implementation of <see cref="IRequestRepository"/> for samples and demos.
/// All data is lost when the process exits. Not safe for concurrent multi-worker deployments.
/// </summary>
internal sealed class InMemoryRequestRepository : IRequestRepository
{
    private readonly ConcurrentDictionary<Guid, RequestAggregate> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _byIdempotencyKey = new();

    public Task AddAsync(RequestAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _byId[aggregate.RequestId] = aggregate;
        if (aggregate.IdempotencyKey is not null)
        {
            _byIdempotencyKey[aggregate.IdempotencyKey] = aggregate.RequestId;
        }

        return Task.CompletedTask;
    }

    public Task<RequestAggregate?> FindByIdAsync(Guid requestId, CancellationToken ct = default)
    {
        _byId.TryGetValue(requestId, out var aggregate);
        return Task.FromResult(aggregate);
    }

    public Task<RequestAggregate?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        RequestAggregate? result = null;
        if (_byIdempotencyKey.TryGetValue(idempotencyKey, out var id))
        {
            _byId.TryGetValue(id, out result);
        }

        return Task.FromResult(result);
    }

    // The aggregate is stored by reference — mutations applied by the worker are immediately
    // visible on the next FindByIdAsync call, so no copy or merge is needed.
    public Task SaveAsync(RequestAggregate aggregate, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<PagedResult<RequestSummaryItem>> ListAsync(RequestFilter filter, CancellationToken ct = default)
    {
        var query = _byId.Values.AsEnumerable();

        if (filter.ClientId is not null)
        {
            query = query.Where(a => a.ClientId == filter.ClientId);
        }

        if (filter.Strategy is not null)
        {
            query = query.Where(a => a.Strategy == filter.Strategy);
        }

        if (filter.Status is not null)
        {
            query = query.Where(a => a.Status == filter.Status);
        }

        if (filter.From is not null)
        {
            query = query.Where(a => a.CreatedAt >= filter.From);
        }

        if (filter.To is not null)
        {
            query = query.Where(a => a.CreatedAt <= filter.To);
        }

        var all = query.OrderByDescending(a => a.CreatedAt).ToList();
        var page = all
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(a => new RequestSummaryItem(
                a.RequestId, a.ClientId, a.Strategy, a.Status,
                a.CurrentState, a.CreatedAt, a.UpdatedAt, a.CompletedAt))
            .ToImmutableList();

        return Task.FromResult(new PagedResult<RequestSummaryItem>(page, all.Count, filter.Page, filter.PageSize));
    }

    public Task<RequestStats> GetStatsAsync(StatsFilter filter, CancellationToken ct = default)
    {
        var aggregates = _byId.Values.ToList();

        var byStrategy = aggregates
            .GroupBy(a => a.Strategy)
            .Select(g => new StrategyStats(
                g.Key,
                Total: g.Count(),
                Completed: g.Count(a => a.Status == RequestStatus.Completed),
                Failed: g.Count(a => a.Status == RequestStatus.Failed),
                InProgress: g.Count(a => a.Status == RequestStatus.InProgress),
                Accepted: g.Count(a => a.Status == RequestStatus.Accepted)))
            .ToImmutableList();

        var stats = new RequestStats(
            Total: aggregates.Count,
            Completed: aggregates.Count(a => a.Status == RequestStatus.Completed),
            Failed: aggregates.Count(a => a.Status == RequestStatus.Failed),
            InProgress: aggregates.Count(a => a.Status == RequestStatus.InProgress),
            Accepted: aggregates.Count(a => a.Status == RequestStatus.Accepted),
            LastProcessedAt: aggregates.Where(a => a.CompletedAt.HasValue).Max(a => a.CompletedAt),
            ByStrategy: byStrategy);

        return Task.FromResult(stats);
    }

    public Task<IReadOnlyList<RequestAggregate>> FindStalledAsync(
        DateTimeOffset leaseExpiredBefore,
        CancellationToken ct = default)
    {
        // Single-worker sample: no real lease tracking, so no stalled steps to recover.
        IReadOnlyList<RequestAggregate> empty = [];
        return Task.FromResult(empty);
    }

    public Task<IReadOnlyList<RequestAggregate>> FindStrandedAsync(CancellationToken ct = default)
    {
        // Single-worker sample: nothing to recover beyond what FindStalledAsync already covers.
        IReadOnlyList<RequestAggregate> empty = [];
        return Task.FromResult(empty);
    }

    public Task<IReadOnlyList<(RequestAggregate Aggregate, RequestStep Step)>> FindDueRetriesAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var results = _byId.Values
            .SelectMany(a => a.Steps
                .Where(s => s.ExecutionStatus == StepExecutionStatus.RetryScheduled
                         && s.NextRetryAt <= now)
                .Select(s => (Aggregate: a, Step: s)))
            .ToList();

        IReadOnlyList<(RequestAggregate, RequestStep)> list = results;
        return Task.FromResult(list);
    }

    // Single-worker sample: always grant the lease — no concurrent workers to protect against.
    public Task<bool> TryAcquireStepLeaseAsync(
        Guid requestId,
        string stateName,
        string workerId,
        TimeSpan duration,
        CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task ReleaseStepLeaseAsync(Guid requestId, string stateName, CancellationToken ct = default) =>
        Task.CompletedTask;
    public Task<RequestAggregate?> FindByProviderResourceNameAsync(string providerName, string resourceName, CancellationToken ct = default)
        => Task.FromResult<RequestAggregate?>(null);
}