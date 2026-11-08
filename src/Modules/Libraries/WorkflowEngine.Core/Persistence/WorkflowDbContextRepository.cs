using System.Collections.Immutable;

using Microsoft.EntityFrameworkCore;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace WorkflowEngine.Core.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IRequestRepository"/> backed by <see cref="WorkflowDbContext"/>.
/// Uses a pooled factory so each operation creates and disposes its own short-lived context.
/// </summary>
internal sealed class WorkflowDbContextRepository : IRequestRepository
{
    private readonly IDbContextFactory<WorkflowDbContext> _factory;

    public WorkflowDbContextRepository(IDbContextFactory<WorkflowDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task AddAsync(RequestAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        ctx.Requests.Add(aggregate);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<RequestAggregate?> FindByIdAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.Requests
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, ct);
    }

    public async Task<RequestAggregate?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.Requests
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task<RequestAggregate?> FindByProviderResourceNameAsync(string providerName, string resourceName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.Requests
         .FromSql($@"
            SELECT r.* FROM workflow.Requests r
            WHERE r.strategy = 'create-project-resource'
              AND JSON_VALUE(r.Payload, '$.ProviderName') = {providerName}
              AND JSON_VALUE(r.Payload, '$.Data.Name') = {resourceName}")
         .Include(r => r.Steps)
         .FirstOrDefaultAsync(ct);
    }

    public async Task SaveAsync(RequestAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        ctx.Requests.Attach(aggregate);
        ctx.Entry(aggregate).State = EntityState.Modified;

        // After Attach, EF Core tracks all steps (via the _steps backing field) as Unchanged —
        // including steps that were just created this unit of work and don't exist in the DB yet.
        // We therefore query the persisted step IDs explicitly so we can distinguish
        // "insert new step" (Added) from "update existing step" (Modified).
        var persistedStepIds = await ctx.RequestSteps
            .Where(s => s.RequestId == aggregate.RequestId)
            .Select(s => s.StepId)
            .ToHashSetAsync(ct);

        foreach (var step in aggregate.Steps)
        {
            ctx.Entry(step).State = persistedStepIds.Contains(step.StepId)
                ? EntityState.Modified
                : EntityState.Added;
        }

        await ctx.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<RequestSummaryItem>> ListAsync(RequestFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var query = ctx.Requests.AsNoTracking();

        if (filter.Status.HasValue)
        {
            query = query.Where(r => r.Status == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Strategy))
        {
            query = query.Where(r => r.Strategy == filter.Strategy);
        }

        if (!string.IsNullOrWhiteSpace(filter.ClientId))
        {
            query = query.Where(r => r.ClientId == filter.ClientId);
        }

        if (filter.From.HasValue)
        {
            query = query.Where(r => r.CreatedAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(r => r.CreatedAt <= filter.To.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new RequestSummaryItem(
                r.RequestId,
                r.ClientId,
                r.Strategy,
                r.Status,
                r.CurrentState,
                r.CreatedAt,
                r.UpdatedAt,
                r.CompletedAt))
            .ToListAsync(ct);

        return new PagedResult<RequestSummaryItem>(
            items.ToImmutableList(),
            totalCount,
            filter.Page,
            filter.PageSize);
    }

    public async Task<RequestStats> GetStatsAsync(StatsFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var query = ctx.Requests.AsNoTracking();

        if (filter.From.HasValue)
        {
            query = query.Where(r => r.CreatedAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(r => r.CreatedAt <= filter.To.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Strategy))
        {
            query = query.Where(r => r.Strategy == filter.Strategy);
        }

        var total = await query.CountAsync(ct);
        var accepted = await query.CountAsync(r => r.Status == RequestStatus.Accepted, ct);
        var inProgress = await query.CountAsync(r => r.Status == RequestStatus.InProgress, ct);
        var completed = await query.CountAsync(r => r.Status == RequestStatus.Completed, ct);
        var failed = await query.CountAsync(r => r.Status == RequestStatus.Failed, ct);

        var byStrategy = await query
            .GroupBy(r => r.Strategy)
            .Select(g => new
            {
                Strategy = g.Key,
                Total = g.Count(),
                Accepted = g.Count(r => r.Status == RequestStatus.Accepted),
                InProgress = g.Count(r => r.Status == RequestStatus.InProgress),
                Completed = g.Count(r => r.Status == RequestStatus.Completed),
                Failed = g.Count(r => r.Status == RequestStatus.Failed),
            })
            .ToListAsync(ct);

        var strategyStats = byStrategy
            .Select(g => new StrategyStats(g.Strategy, g.Total, g.Completed, g.Failed, g.InProgress, g.Accepted))
            .ToImmutableList();

        return new RequestStats(total, completed, failed, inProgress, accepted, null, strategyStats);
    }

    public async Task<IReadOnlyList<RequestAggregate>> FindStalledAsync(
        DateTimeOffset leaseExpiredBefore,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        return await ctx.Requests
            .Include(r => r.Steps)
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.Steps.Any(s => s.ExecutionStatus == StepExecutionStatus.Running
                                      && s.LockedUntil < leaseExpiredBefore))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RequestAggregate>> FindStrandedAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        return await ctx.Requests
            .Include(r => r.Steps)
            .Where(r =>
                (r.Status == RequestStatus.Accepted && !r.Steps.Any())
                || (r.Status == RequestStatus.InProgress
                    && !r.Steps.Any(s => s.ExecutionStatus == StepExecutionStatus.Running)
                    && !r.Steps.Any(s => s.ExecutionStatus == StepExecutionStatus.RetryScheduled)))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(RequestAggregate Aggregate, RequestStep Step)>> FindDueRetriesAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var aggregates = await ctx.Requests
            .Include(r => r.Steps)
            .Where(r => r.Steps.Any(s => s.ExecutionStatus == StepExecutionStatus.RetryScheduled
                                      && s.NextRetryAt <= now))
            .ToListAsync(ct);

        var result = new List<(RequestAggregate, RequestStep)>();

        foreach (var aggregate in aggregates)
        {
            var dueSteps = aggregate.Steps
                .Where(s => s.ExecutionStatus == StepExecutionStatus.RetryScheduled
                         && s.NextRetryAt <= now);

            foreach (var step in dueSteps)
            {
                result.Add((aggregate, step));
            }
        }

        return result.AsReadOnly();
    }

    public async Task<bool> TryAcquireStepLeaseAsync(
        Guid requestId,
        string stateName,
        string workerId,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.Add(duration);

        // The step's row is written by RecordStepStarted, which the caller runs only after the lease is
        // granted — so on a step's first execution there is nothing to update. Without this branch that
        // reads as "held by another worker" and the step is dropped, stranding the request forever.
        // Racing workers are serialised by the aggregate's rowversion on the SaveAsync that follows.
        var stepExists = await ctx.RequestSteps
            .AnyAsync(s => s.RequestId == requestId && s.StepName == stateName, ct);

        if (!stepExists)
        {
            return true;
        }

        var rows = await ctx.RequestSteps
            .Where(s => s.RequestId == requestId
                     && s.StepName == stateName
                     && (s.LockedUntil == null || s.LockedUntil < now || s.LockedBy == workerId))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.LockedBy, workerId)
                    .SetProperty(s => s.LockedUntil, leaseUntil),
                ct);

        return rows > 0;
    }

    public async Task ReleaseStepLeaseAsync(Guid requestId, string stateName, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        await ctx.RequestSteps
            .Where(s => s.RequestId == requestId && s.StepName == stateName)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.LockedBy, (string?)null)
                    .SetProperty(s => s.LockedUntil, (DateTimeOffset?)null),
                ct);
    }
}