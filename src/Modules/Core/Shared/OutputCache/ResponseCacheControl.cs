using Microsoft.AspNetCore.Http;

namespace Core.Shared.OutputCache;

/// <summary>
/// Per-request switch an endpoint can flip to keep its (otherwise 200) response out of the
/// output cache — e.g. when the body reflects a transient upstream failure that must not be
/// cached and served to later callers. <see cref="CoreCachePolicy"/> honours it at storage time.
/// </summary>
internal static class ResponseCacheControl
{
    private const string SuppressKey = "__core.outputcache.suppressStorage";

    internal static void SuppressStorage(HttpContext context)
        => context.Items[SuppressKey] = true;

    internal static bool IsStorageSuppressed(HttpContext context)
        => context.Items[SuppressKey] is true;
}