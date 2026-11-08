using Microsoft.AspNetCore.Http;

namespace Core.Shared.OutputCache;

/// <summary>
/// Per-request channel for response-derived cache tags. Any endpoint can stash tags here
/// (computed from its actual response, not from request data alone); <see cref="CoreCachePolicy"/>
/// merges them into <see cref="Microsoft.AspNetCore.OutputCaching.OutputCacheContext.Tags"/> at
/// storage time so they are honoured by eviction without an extra DB query.
/// </summary>
internal static class ResponseCacheTags
{
    private const string ItemsKey = "__core.outputcache.responseTags";

    internal static void Add(HttpContext context, IEnumerable<string> tags)
    {
        if (context.Items[ItemsKey] is not HashSet<string> set)
            context.Items[ItemsKey] = set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
            set.Add(tag);
    }

    internal static IReadOnlyCollection<string> Get(HttpContext context)
        => context.Items[ItemsKey] as HashSet<string> ?? (IReadOnlyCollection<string>)[];
}