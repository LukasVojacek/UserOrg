using System.Collections.Concurrent;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace Core.UnitTests.ResourceRequests.Flow;

/// <summary>
/// Minimal in-memory <see cref="IRequestRepository"/> for the assign-resource flow e2e (mirrors the engine's own
/// <c>GatedFlowEndToEndTests</c> helper): live aggregate instances so worker mutations are visible, and step
/// leases granted unconditionally (single worker → no contention). Query/stats paths are unused here.
/// </summary>
internal sealed class InMemoryWorkflowRepository : IRequestRepository
{
    private readonly ConcurrentDictionary<Guid, RequestAggregate> _store = new();

    public Task AddAsync(RequestAggregate aggregate, CancellationToken ct = default)
    {
        aggregate.RowVersion = [1];
        _store[aggregate.RequestId] = aggregate;
        return Task.CompletedTask;
    }

    public Task<RequestAggregate?> FindByIdAsync(Guid requestId, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(requestId));

    public Task<RequestAggregate?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
        => Task.FromResult(_store.Values.FirstOrDefault(a => a.IdempotencyKey == idempotencyKey));

    public Task SaveAsync(RequestAggregate aggregate, CancellationToken ct = default)
    {
        _store[aggregate.RequestId] = aggregate;
        return Task.CompletedTask;
    }

    public Task<PagedResult<RequestSummaryItem>> ListAsync(RequestFilter filter, CancellationToken ct = default)
        => Task.FromResult(new PagedResult<RequestSummaryItem>([], 0, filter.Page, filter.PageSize));

    public Task<RequestStats> GetStatsAsync(StatsFilter filter, CancellationToken ct = default)
        => Task.FromResult(new RequestStats(0, 0, 0, 0, 0, null, []));

    public Task<IReadOnlyList<RequestAggregate>> FindStalledAsync(DateTimeOffset leaseExpiredBefore, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RequestAggregate>>([]);

    public Task<IReadOnlyList<RequestAggregate>> FindStrandedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RequestAggregate>>([]);

    public Task<IReadOnlyList<(RequestAggregate Aggregate, RequestStep Step)>> FindDueRetriesAsync(DateTimeOffset now, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<(RequestAggregate, RequestStep)>>([]);

    public Task<bool> TryAcquireStepLeaseAsync(Guid requestId, string stateName, string workerId, TimeSpan duration, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task ReleaseStepLeaseAsync(Guid requestId, string stateName, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<RequestAggregate?> FindByProviderResourceNameAsync(string providerName, string resourceName, CancellationToken ct = default)
        => Task.FromResult<RequestAggregate?>(null);
}

/// <summary>No-op <see cref="ICallbackPort"/> — the worker requires one but the e2e asserts on projection state.</summary>
internal sealed class NoopCallbackPort : ICallbackPort
{
    public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default) => Task.CompletedTask;
}
