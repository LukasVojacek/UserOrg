using Waldur.Sync.Diff;

using Xunit;

namespace Waldur.Sync.Tests.Diff;

public sealed class ShaBreadcrumbMapTests
{
    [Fact]
    public void Should_ApplySetMutation_ToStoreHash()
    {
        var map = new ShaBreadcrumbMap();
        var key = ShaKey.Provider("p1");

        map.Apply(ShaMutation.Set(key, "abc"));

        Assert.True(map.TryGetHash(key, out var hash));
        Assert.Equal("abc", hash);
        Assert.Equal("abc", map.GetHashOrNull(key));
        Assert.Equal(1, map.Count);
    }

    [Fact]
    public void Should_ApplyRemoveMutation_ToDropHash()
    {
        var key = ShaKey.Member("proj", "alice");
        var map = new ShaBreadcrumbMap();
        map.Apply(ShaMutation.Set(key, "abc"));

        map.Apply(ShaMutation.Remove(key));

        Assert.False(map.TryGetHash(key, out _));
        Assert.Null(map.GetHashOrNull(key));
        Assert.Equal(0, map.Count);
    }

    [Fact]
    public void Should_SeedFromDictionary_ForColdRebuild()
    {
        var key = ShaKey.Project("p1");
        var seed = new Dictionary<ShaKey, string> { [key] = "seed" };

        var map = new ShaBreadcrumbMap(seed);

        Assert.Equal("seed", map.GetHashOrNull(key));
    }

    [Fact]
    public void Should_ExposeKeys_ForAbsenceSweep()
    {
        var map = new ShaBreadcrumbMap();
        map.Apply(ShaMutation.Set(ShaKey.Provider("p1"), "a"));
        map.Apply(ShaMutation.Set(ShaKey.Project("q1"), "b"));

        Assert.Contains(ShaKey.Provider("p1"), map.Keys);
        Assert.Contains(ShaKey.Project("q1"), map.Keys);
    }

    [Fact]
    public void Should_TreatStorageConfigSlot_AsNotColdRebuildable()
    {
        Assert.False(ShaKey.IsColdRebuildable(ShaKey.StorageConfig()));
        Assert.True(ShaKey.IsColdRebuildable(ShaKey.Provider("p1")));
        Assert.True(ShaKey.IsColdRebuildable(ShaKey.Project("q1")));
        Assert.True(ShaKey.IsColdRebuildable(ShaKey.Member("q1", "alice")));
    }

    [Fact]
    public void Should_BuildStorageConfigKey_WithConfigSlot()
    {
        var key = ShaKey.StorageConfig();

        Assert.Equal(WaldurEntityType.StorageConfig, key.EntityType);
        Assert.Equal(ShaKey.StorageConfigSlot, key.ExternalId);
        Assert.Null(key.UserName);
    }
}
