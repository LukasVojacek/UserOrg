using Microsoft.AspNetCore.OutputCaching;

namespace Core.Shared.OutputCache;

internal sealed class OutputCacheInvalidator(IOutputCacheStore store) : IOutputCacheInvalidator
{
    public ValueTask EvictAsync(string tag, CancellationToken ct = default)
        => store.EvictByTagAsync(tag, ct);
}