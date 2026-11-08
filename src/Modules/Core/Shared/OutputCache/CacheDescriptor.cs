using Microsoft.AspNetCore.Http;

namespace Core.Shared.OutputCache;

/// <summary>
/// Describes how a single request should be cached.
/// <para>
/// <see cref="VaryByValues"/> decide the cache <b>key</b> — which distinct entries exist.
/// <see cref="Tags"/> decide invalidation <b>groups</b> — which entries are evicted together.
/// The two axes are independent.
/// </para>
/// </summary>
internal sealed record CacheDescriptor(
    TimeSpan Expiration,
    IReadOnlyDictionary<string, string> VaryByValues,
    IReadOnlyList<string> Tags);

/// <summary>
/// Per-endpoint strategy that builds a <see cref="CacheDescriptor"/> for the current request.
/// Return <c>null</c> to skip caching this request (e.g. a required key is missing).
/// The delegate is async so it can resolve scoped services via <c>context.RequestServices</c>.
/// </summary>
internal delegate ValueTask<CacheDescriptor?> DescribeCacheAsync(HttpContext context, CancellationToken ct);