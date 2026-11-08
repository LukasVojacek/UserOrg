using Waldur.Contracts.Events;
using Waldur.Sync.Diff;

using Xunit;

namespace Waldur.Sync.Tests.Diff;

public sealed class DiffEngineStorageConfigTests
{
    [Fact]
    public void Should_EmitStorageConfigurationChanged_When_SlotUnknown()
    {
        var config = DiffTestData.StorageConfig();
        var input = new DiffInput { StorageConfig = config };

        var result = DiffTestData.Engine().Diff(input, new ShaBreadcrumbMap());

        var action = Assert.Single(result.Actions);
        var changed = Assert.IsType<StorageConfigurationChanged>(action.Event);
        Assert.Same(config, changed.Config);
        Assert.Equal(ShaKey.StorageConfig(), action.OnAck.Key);
        Assert.Equal(config.ComputeIdentityHash(), action.OnAck.Hash);
    }

    [Fact]
    public void Should_EmitStorageConfigurationChanged_When_ConfigHashDiffers()
    {
        var old = DiffTestData.StorageConfig("EFP-Core-Old");
        var current = DiffTestData.StorageConfig("EFP-Core-New");
        var map = DiffTestData.MapWith((ShaKey.StorageConfig(), old));
        var input = new DiffInput { StorageConfig = current };

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.IsType<StorageConfigurationChanged>(Assert.Single(result.Actions).Event);
    }

    [Fact]
    public void Should_EmitNothing_When_StorageConfigUnchanged()
    {
        var config = DiffTestData.StorageConfig();
        var map = DiffTestData.MapWith((ShaKey.StorageConfig(), config));
        var input = new DiffInput { StorageConfig = config };

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Should_NeverEmitStorage_When_ConfigAbsent()
    {
        var input = new DiffInput { StorageConfig = null };

        var result = DiffTestData.Engine().Diff(input, new ShaBreadcrumbMap());

        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Should_ExcludeStorageSlot_FromColdRebuildContract()
    {
        // A cold rebuild is defined as the subset of keys that IShaMapRebuilder is allowed to return.
        var allKeys = new[]
        {
            ShaKey.Provider("p1"),
            ShaKey.Project("q1"),
            ShaKey.Member("q1", "alice"),
            ShaKey.StorageConfig()
        };

        var rebuildable = allKeys.Where(ShaKey.IsColdRebuildable).ToList();

        Assert.DoesNotContain(ShaKey.StorageConfig(), rebuildable);
        Assert.Equal(3, rebuildable.Count);
    }
}
