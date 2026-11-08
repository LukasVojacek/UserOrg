using System.Collections.Concurrent;

using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.Extensions.Caching.Hybrid
{
    // Minimal concrete implementation placed in UserOrg.Shared so tests can reference the type.
    internal sealed class ConcreteHybridCache : HybridCache
    {
        private readonly ConcurrentDictionary<string, (object Value, DateTimeOffset? Expiry)> _store = new();
        private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _tags = new();
        private readonly MemoryCache _mem = new(new MemoryCacheOptions());

        public ConcreteHybridCache()
        {
        }

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            if (_store.TryGetValue(key, out var existing))
            {
                if (existing.Expiry.HasValue && existing.Expiry.Value <= DateTimeOffset.UtcNow)
                {
                    _store.TryRemove(key, out _);
                }
                else
                {
                    return (T)existing.Value!;
                }
            }

            var cacheEntry = _mem.GetOrCreate(key, entry =>
            {
                if (options?.Expiration is TimeSpan exp)
                {
                    entry.AbsoluteExpirationRelativeToNow = exp;
                }
                return new Lazy<Task<object>>(async () =>
                {
                    var v = await factory(state, cancellationToken);
                    return (object)v!;
                });
            });

            var lazy = (Lazy<Task<object>>)cacheEntry;
            var resultObj = await lazy.Value;

            DateTimeOffset? expiry = null;
            if (options?.Expiration is TimeSpan ts)
            {
                expiry = DateTimeOffset.UtcNow.Add(ts);
            }

            _store[key] = (resultObj, expiry);

            if (tags is not null)
            {
                foreach (var t in tags)
                {
                    var bag = _tags.GetOrAdd(t, _ => new ConcurrentBag<string>());
                    bag.Add(key);
                }
            }

            return (T)resultObj;
        }

        // Convenience overload used in codebase - hide base method
        public new ValueTask<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            return GetOrCreateAsync<object, T>(key, null!, (s, ct) => factory(ct), options, tags, cancellationToken);
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
            DateTimeOffset? expiry = null;
            if (options?.Expiration is TimeSpan ts)
            {
                expiry = DateTimeOffset.UtcNow.Add(ts);
            }

            _store[key] = (value!, expiry);

            if (tags is not null)
            {
                foreach (var t in tags)
                {
                    var bag = _tags.GetOrAdd(t, _ => new ConcurrentBag<string>());
                    bag.Add(key);
                }
            }

            _mem.Set(key, new Lazy<Task<object>>(() => Task.FromResult((object)value!)), new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiry.HasValue ? expiry - DateTimeOffset.UtcNow : null });
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key is null)
                throw new ArgumentNullException(nameof(key));
            _store.TryRemove(key, out _);
            _mem.Remove(key);
            return ValueTask.CompletedTask;
        }

        public new ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            if (keys is null)
                throw new ArgumentNullException(nameof(keys));
            foreach (var k in keys)
            {
                _store.TryRemove(k, out _);
                _mem.Remove(k);
            }
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            if (tag is null)
                throw new ArgumentNullException(nameof(tag));
            if (_tags.TryRemove(tag, out var bag))
            {
                foreach (var k in bag)
                {
                    _store.TryRemove(k, out _);
                    _mem.Remove(k);
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}