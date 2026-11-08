using System.Collections.Concurrent;

using Microsoft.Extensions.Caching.Hybrid;

namespace Heappe.UnitTests;

/// <summary>
/// Minimal in-memory <see cref="HybridCache"/> for unit tests. Supports GetOrCreate semantics and
/// tag-based eviction so caching/invalidation behavior can be asserted without external infrastructure.
/// </summary>
internal sealed class FakeHybridCache : HybridCache
{
    private readonly ConcurrentDictionary<string, object?> _store = new();
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _tags = new();

    public override async ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var existing))
        {
            return (T)existing!;
        }

        var value = await factory(state, cancellationToken);
        _store[key] = value;

        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                _tags.GetOrAdd(tag, _ => []).Add(key);
            }
        }

        return value;
    }

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        _store[key] = value;
        if (tags is not null)
        {
            foreach (var tag in tags)
            {
                _tags.GetOrAdd(tag, _ => []).Add(key);
            }
        }

        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (_tags.TryRemove(tag, out var keys))
        {
            foreach (var key in keys)
            {
                _store.TryRemove(key, out _);
            }
        }

        return ValueTask.CompletedTask;
    }
}
