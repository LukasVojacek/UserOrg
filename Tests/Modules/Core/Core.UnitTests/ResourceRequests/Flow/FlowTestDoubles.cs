using Core.DataAccess.ResourceRequests;
using Core.Domain.Primitives;
using Core.Domain.Provider;

namespace Core.UnitTests.ResourceRequests.Flow;

/// <summary>In-memory <see cref="IResourceRequestProjectionStore"/> for flow handler + e2e tests.</summary>
internal sealed class FakeProjectResourceRequestStore : IResourceRequestProjectionStore
{
    private readonly Dictionary<string, ProjectResourceRequest> _rows = new();

    public FakeProjectResourceRequestStore Seed(ProjectResourceRequest row)
    {
        _rows[row.Slug] = row;
        return this;
    }

    public ProjectResourceRequest Row(string slug) => _rows[slug];

    public Task<ProjectResourceRequest?> GetBySlugAsync(string slug, CancellationToken ct)
        => Task.FromResult(_rows.GetValueOrDefault(slug));

    public Task InsertAsync(ProjectResourceRequest row, CancellationToken ct)
    {
        _rows[row.Slug] = row;
        return Task.CompletedTask;
    }

    public Task DeleteBySlugAsync(string slug, CancellationToken ct)
    {
        _rows.Remove(slug);
        return Task.CompletedTask;
    }

    public Task UpdateBySlugAsync(string slug, Action<ProjectResourceRequest> mutate, CancellationToken ct)
    {
        if (!_rows.TryGetValue(slug, out var row))
        {
            throw new ResourceRequestRowMissingException(slug);
        }

        mutate(row);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Minimal <see cref="IRepository{TAggregate,TKey}"/> returning a single aggregate. <see cref="Item"/> is
/// settable so a test can let an aggregate appear mid-flow, the way a provider creates a resource while an
/// assignment request is parked.
/// </summary>
internal sealed class FakeRepository<TAggregate, TKey>(TAggregate? item) : IRepository<TAggregate, TKey>
    where TAggregate : AggregateRoot
{
    public TAggregate? Item { get; set; } = item;

    public ValueTask<TAggregate?> FindAsync(TKey key, CancellationToken cancellationToken) => new(Item);

    public ValueTask<TAggregate?> AddAsync(TAggregate aggregate, CancellationToken cancellationToken) => new(aggregate);

    public void Delete(TAggregate aggregate, CancellationToken cancellationToken) { }
}

/// <summary>Provider repository fake returning a single preset provider (used by the provider-notify handler).</summary>
internal sealed class FakeProviderRepository(Provider? provider) : IProviderRepository
{
    public ValueTask<Provider?> FindAsync(ProviderName key, CancellationToken cancellationToken) => new(provider);

    public ValueTask<Provider?> AddAsync(Provider aggregate, CancellationToken cancellationToken) => new(aggregate);

    public void Delete(Provider aggregate, CancellationToken cancellationToken) { }

    public ValueTask<Provider?> FindIncludingDeactivatedAsync(ProviderName key, CancellationToken cancellationToken) => new(provider);

    public ValueTask<Provider?> FindWithAllocatedResourcesAsync(ProviderName key, CancellationToken cancellationToken) => new(provider);

    public void MarkAssignedLocationAsAdded(AssignedLocation assignedLocation) { }
}
