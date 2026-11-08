namespace Core.Shared.OutputCache;

/// <summary>
/// Evicts cached responses by tag. Build the tag with <see cref="CacheTags"/> so the
/// eviction target always matches what the policy tagged, e.g.
/// <c>invalidator.EvictAsync(CacheTags.Project(shortName), ct)</c>.
/// </summary>
internal interface IOutputCacheInvalidator
{
    ValueTask EvictAsync(string tag, CancellationToken ct = default);
}