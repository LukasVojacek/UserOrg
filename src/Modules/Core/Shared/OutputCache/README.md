# Output Cache

Central, reusable output caching for Core endpoints. One configurable policy
(`CoreCachePolicy`) driven by a per-endpoint `DescribeCacheAsync` delegate, plus
tag-based invalidation via `IOutputCacheInvalidator`.

## Concepts

- **Vary-by** (`CacheDescriptor.VaryByValues`) decides the cache **key** — which
  distinct entries exist (e.g. one per `sub`, per `permissions` hash, per `shortName`).
- **Tags** (`CacheDescriptor.Tags`) decide invalidation **groups** — which entries
  are evicted together. Tags are independent of vary-by.
- All tag strings come from `CacheTags` — never hand-write a tag string.

## Adding cache to a new endpoint

1. Add a tag factory to `CacheTags` for the resource the endpoint depends on:

   ```csharp
   public static string Order(string orderId) => $"order:{orderId}";
   ```

2. Create a `XxxCache` static class next to the feature with a policy name and a
   describe delegate. Return `null` to skip caching (e.g. when the key is missing):

   ```csharp
   internal static class OrderCache
   {
       internal const string PolicyName = "Order";

       internal static ValueTask<CacheDescriptor?> Describe(HttpContext ctx, CancellationToken ct)
       {
           if (!ctx.Request.RouteValues.TryGetValue("orderId", out var id) || id is null)
               return ValueTask.FromResult<CacheDescriptor?>(null);

           return ValueTask.FromResult<CacheDescriptor?>(new CacheDescriptor(
               Expiration: TimeSpan.FromMinutes(5),
               VaryByValues: new Dictionary<string, string>(),
               Tags: [CacheTags.Order(id.ToString()!)]));
       }
   }
   ```

   Need data to build the key/tag (e.g. permissions)? The delegate is async and can
   resolve scoped services via `ctx.RequestServices.GetRequiredService<T>()`.

3. Register the policy in `ServiceCollectionExtensions.AddOutputCache`:

   ```csharp
   options.AddPolicy(OrderCache.PolicyName, new CoreCachePolicy(OrderCache.Describe));
   ```

4. Attach it on the endpoint: `.CacheOutput(OrderCache.PolicyName)`.

### Tagging across endpoints

Any endpoint whose response depends on a resource should add that resource's tag —
even list/search endpoints. An entry may carry several tags:

```csharp
Tags: [CacheTags.Order(orderId), CacheTags.UserInfo(sub)]
```

Such an entry is evicted when **either** tag is evicted.

## Resetting (invalidating) the cache

Invalidation is automatic via domain events. The cleanest pattern is a marker interface
on the event plus a generic eviction handler, registered per closed generic so each
registration implements exactly one `IDomainEventHandler<T>` (avoids default-interface-method
ambiguity):

```csharp
// 1. Mark the event with the identifier used in the tag.
public interface IOrderScopedEvent { OrderId OrderId { get; } }
public sealed record OrderUpdatedEvent(OrderId OrderId) : IDomainEvent, IOrderScopedEvent;

// 2. One generic handler for all order-scoped events.
internal sealed class OrderCacheEvictionHandler<TEvent>(IOutputCacheInvalidator cache)
    : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent, IOrderScopedEvent
{
    public Task HandleAsync(TEvent e, CancellationToken ct = default)
        => cache.EvictAsync(CacheTags.Order(e.OrderId.Value), ct).AsTask();
}

// 3. Register per event type in ServiceCollectionExtensions.
services.AddScoped<IDomainEventHandler<OrderUpdatedEvent>, OrderCacheEvictionHandler<OrderUpdatedEvent>>();
```

Eviction runs in the background dispatcher (`DomainEventDispatcherService`) with error
isolation — a failed eviction is logged, not thrown, and never breaks the request.

### Response-derived tags

Some tags are only known after the handler runs — for example, the set of resource and
location IDs actually present in a response. Use `ResponseCacheTags.Add(httpContext, tags)`
inside the handler (or at the end of `HandleAsync`) to stash them; `CoreCachePolicy`
merges them into the entry at storage time (in `ServeResponseAsync`).

```csharp
// In HandleAsync, after building the response:
ResponseCacheTags.Add(http, entries.SelectMany(e =>
    new[] { CacheTags.ProjectResource(e.ResourceId), CacheTags.Location(e.LocationId) }));
```

The tags are stored with the cache entry automatically — no extra DB query, no custom policy
class needed. Eviction triggers for these tags should be wired when the owning write-feature
migrates into the new module and raises domain events (or by calling
`IOutputCacheInvalidator.EvictAsync` directly from legacy write paths). Until then the
5-minute TTL bounds staleness.

Need to invalidate from a non-event code path (e.g. a write endpoint)? Inject
`IOutputCacheInvalidator` and call it directly:

```csharp
await cache.EvictAsync(CacheTags.Order(orderId), ct);
```

> The domain event must carry the identifier used in the tag. If it doesn't (e.g. the
> event has a name but the tag uses an id), enrich the event — see how the user events
> were extended with `IdpId` so `userinfo:{sub}` could be evicted precisely.
