namespace Core.Shared.OutputCache;

/// <summary>
/// Single source of truth for output-cache tag strings. Both the cache policies
/// (when tagging a response) and <see cref="IOutputCacheInvalidator"/> (when evicting)
/// build tags from here so the two never drift apart.
/// </summary>
internal static class CacheTags
{
    /// <summary>
    /// Global tag carried by every cacheable v2 response, so the whole output cache can be flushed with a
    /// single <see cref="IOutputCacheInvalidator.EvictAsync"/> call (the ResetAllCaches admin endpoint).
    /// Output cache eviction is tag-based and the per-entity tags are parameterised, so a shared tag is the
    /// only way to evict everything at once.
    /// </summary>
    public const string All = "all";

    public static string UserInfo(string sub) => $"userinfo:{sub}";

    public static string Project(string shortName) => $"project:{shortName}";

    public static string ProjectResource(Guid resourceId) => $"resource:{resourceId}";

    public static string Location(string locationId) => $"location:{locationId}";
}