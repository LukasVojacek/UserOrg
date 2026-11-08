using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;

namespace Core.Shared.OutputCache;

/// <summary>
/// The single configurable output-cache policy for Core endpoints. All the boilerplate
/// (enable/allow flags, store-only-on-200, vary-by and tag wiring) lives here; each endpoint
/// just supplies a <see cref="DescribeCacheAsync"/> delegate describing what to cache.
/// </summary>
internal sealed class CoreCachePolicy(DescribeCacheAsync describe) : IOutputCachePolicy
{
    public async ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var descriptor = await describe(context.HttpContext, cancellationToken);
        if (descriptor is null)
        {
            context.EnableOutputCaching = false;
            return;
        }

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = descriptor.Expiration;

        // Global tag so ResetAllCaches can flush the whole output cache in one EvictByTag call.
        context.Tags.Add(CacheTags.All);

        foreach (var (key, value) in descriptor.VaryByValues)
        {
            context.CacheVaryByRules.VaryByValues[key] = value;
        }

        foreach (var tag in descriptor.Tags)
        {
            context.Tags.Add(tag);
        }
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var store = context.HttpContext.Response.StatusCode == StatusCodes.Status200OK
            && !ResponseCacheControl.IsStorageSuppressed(context.HttpContext);
        context.AllowCacheStorage = store;
        if (store)
            foreach (var tag in ResponseCacheTags.Get(context.HttpContext))
                context.Tags.Add(tag);
        return ValueTask.CompletedTask;
    }
}