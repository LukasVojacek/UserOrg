using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Features.CommandTemplatePermissions;

using Microsoft.Extensions.Caching.Hybrid;

using Xunit;

namespace Heappe.UnitTests.Features.CommandTemplatePermissions;

/// <summary>
/// Witnesses the producer-to-evictor wiring across the global kill-switch cache tag (ADR-0077 §4):
/// the tags a cached permission entry actually carries come from the REAL
/// <see cref="CommandTemplatePermissionQueryHandler.BuildPermissionTags"/> (the producer's exact
/// tag-attaching call site), and the entry is dropped by the REAL <c>RemoveByTagAsync</c> call the
/// evictor (<c>KillSwitchAdminHandler.EvictPermissionCacheAsync</c>) makes.
/// </summary>
/// <remarks>
/// Neither B04's nor B09's own tests exercise this seam together: B04 asserts
/// <c>BuildPermissionTags</c>'s return value directly, and B09 hand-seeds cache entries carrying the
/// tag literal rather than deriving it from the producer. Deleting
/// <c>.Append(HeappeCacheKeys.KillSwitchGlobalTag)</c> from <c>BuildPermissionTags</c> leaves both of
/// those tests green while silently reopening the 5-minute kill-switch launch window; this test fails
/// under that mutation because the tags fed into <c>GetOrCreateAsync</c> below are the producer's own
/// output, not a hand-written literal.
/// <para>
/// The full <c>Handle</c> path cannot be driven end-to-end here with non-empty allocations: EF InMemory
/// (and SQLite) cannot translate the <c>SelectMany</c>-over-navigation projection in
/// <c>ComposeModelAsync</c> (<c>NotImplementedException</c> at query-compile time regardless of data),
/// so this test drives the narrowest real seam instead — the producer's real tag computation feeding a
/// real cache round-trip, evicted by the real tag the evictor removes.
/// </para>
/// </remarks>
public sealed class CommandTemplatePermissionCacheEvictionWiringTests
{
    private static readonly Guid _projectId = Guid.NewGuid();

    [Fact(DisplayName = "A cache entry tagged via the real producer's BuildPermissionTags is gone after RemoveByTagAsync(KillSwitchGlobalTag)")]
    public async Task Producer_tagged_entry_is_evicted_by_the_global_tag()
    {
        var cache = new FakeHybridCache();
        var allocations = new List<ProjectAllocationInfoEntry>
        {
            new(_projectId, "proj-1", [])
        };

        // The producer's REAL tag-attaching call site (CommandTemplatePermissionQueryHandler line
        // `.Append(HeappeCacheKeys.KillSwitchGlobalTag)`), not a hand-written tag literal.
        var tags = CommandTemplatePermissionQueryHandler.BuildPermissionTags(allocations);

        var calls = 0;
        var cacheOptions = new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) };

        async ValueTask<string> Factory(CancellationToken _)
        {
            calls++;
            return "computed-permissions";
        }

        // Mirrors the exact call the producer makes at its GetOrCreateAsync call site: same key
        // builder, same tags array, same options shape.
        await cache.GetOrCreateAsync(
            HeappeCacheKeys.Permissions("user-1", "heappe-1"),
            Factory,
            cacheOptions,
            tags: tags,
            cancellationToken: CancellationToken.None);
        Assert.Equal(1, calls);

        // Still cached: a second call within the TTL must NOT re-invoke the factory.
        await cache.GetOrCreateAsync(
            HeappeCacheKeys.Permissions("user-1", "heappe-1"),
            Factory,
            cacheOptions,
            tags: tags,
            cancellationToken: CancellationToken.None);
        Assert.Equal(1, calls);

        // The REAL evictor call: KillSwitchAdminHandler.EvictPermissionCacheAsync does exactly this.
        await cache.RemoveByTagAsync(HeappeCacheKeys.KillSwitchGlobalTag, CancellationToken.None);

        // Gone: the factory must be re-invoked. If the producer's Append(KillSwitchGlobalTag) were
        // removed, `tags` above would never have carried the global tag, this RemoveByTagAsync would
        // be a no-op against this entry, and the assertion below would fail (calls would stay 1).
        await cache.GetOrCreateAsync(
            HeappeCacheKeys.Permissions("user-1", "heappe-1"),
            Factory,
            cacheOptions,
            tags: tags,
            cancellationToken: CancellationToken.None);
        Assert.Equal(2, calls);
    }
}
