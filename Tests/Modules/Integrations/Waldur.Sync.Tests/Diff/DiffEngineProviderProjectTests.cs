using Waldur.Contracts.Events;
using Waldur.Sync.Diff;
using Waldur.Sync.Tests.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Diff;

public sealed class DiffEngineProviderProjectTests
{
    [Fact]
    public void Should_EmitProviderCreated_When_IdUnknown()
    {
        var provider = DiffTestData.Provider();
        var input = new DiffInput { Providers = [new ProviderDiffInput(provider.ExternalId, provider)] };

        var result = DiffTestData.Engine().Diff(input, new ShaBreadcrumbMap());

        var action = Assert.Single(result.Actions);
        var created = Assert.IsType<ProviderCreated>(action.Event);
        Assert.Same(provider, created.Payload);
        Assert.Equal(DiffTestData.Instance, created.Instance);
        Assert.Equal(DiffTestData.ServiceName, created.ServiceName);
        Assert.Equal(ShaMutationKind.Set, action.OnAck.Kind);
        Assert.Equal(ShaKey.Provider(provider.ExternalId), action.OnAck.Key);
        Assert.Equal(provider.ComputeIdentityHash(), action.OnAck.Hash);
    }

    [Fact]
    public void Should_EmitProviderUpdated_When_HashDiffers()
    {
        var old = DiffTestData.Provider(name: "Old");
        var current = DiffTestData.Provider(name: "New");
        var map = DiffTestData.MapWith((ShaKey.Provider(current.ExternalId), old));
        var input = new DiffInput { Providers = [new ProviderDiffInput(current.ExternalId, current)] };

        var result = DiffTestData.Engine().Diff(input, map);

        var action = Assert.Single(result.Actions);
        Assert.IsType<ProviderUpdated>(action.Event);
        Assert.Equal(current.ComputeIdentityHash(), action.OnAck.Hash);
    }

    [Fact]
    public void Should_EmitNothing_When_ProviderHashEqual()
    {
        var provider = DiffTestData.Provider();
        var map = DiffTestData.MapWith((ShaKey.Provider(provider.ExternalId), provider));
        var input = new DiffInput { Providers = [new ProviderDiffInput(provider.ExternalId, provider)] };

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Actions);
        Assert.Empty(result.Absences);
    }

    [Fact]
    public void Should_EmitProviderUpdated_Once_Then_Converge_When_StoredProviderNameWasDirty()
    {
        // D3/ADR-0055: WaldurShaMapRebuilder reads names back from the DB verbatim ("Stored shapes
        // read as-is", WaldurShaMapRebuilder.cs remarks §5), so a provider row stored before the
        // mapper's strip rebuilds to a dirty breadcrumb. That must cost exactly one Updated, not a
        // standing storm — and it converges in production too, because the consumer matches a
        // provider by ExternalId and renames it in place (unlike ProjectResource.Name, the resource's
        // own upsert key, which D11 therefore leaves unstripped). The clean side comes out of the
        // REAL mapper, so reverting B02's provider strip turns this red.
        const string dirtyProviderName = "Provider\u0007 Inc\u0000";
        var clean = MapperTestData.Mapper()
            .MapProvider(MapperTestData.Customer(name: dirtyProviderName), [MapperTestData.Offering()])
            .Payload!;

        var storedDirty = clean with { Name = dirtyProviderName };
        var map = DiffTestData.MapWith((ShaKey.Provider(clean.ExternalId), storedDirty));
        var input = new DiffInput { Providers = [new ProviderDiffInput(clean.ExternalId, clean)] };

        // First pass: the mismatch between the dirty breadcrumb and the clean input → one ProviderUpdated.
        var first = DiffTestData.Engine().Diff(input, map);
        var action = Assert.Single(first.Actions);
        Assert.IsType<ProviderUpdated>(action.Event);
        Assert.Equal(clean.ComputeIdentityHash(), action.OnAck.Hash);

        // Apply the ack (as SyncFrom having written the clean name would), then re-diff → Unchanged.
        map.Apply(action.OnAck);
        var second = DiffTestData.Engine().Diff(input, map);
        Assert.Empty(second.Actions);
    }

    [Fact]
    public void Should_EmitProjectCreated_When_IdUnknown()
    {
        var project = DiffTestData.Project();
        var input = new DiffInput { Projects = [new ProjectDiffInput(project.ExternalId, project)] };

        var result = DiffTestData.Engine().Diff(input, new ShaBreadcrumbMap());

        var action = Assert.Single(result.Actions);
        var created = Assert.IsType<ProjectCreated>(action.Event);
        Assert.Same(project, created.Payload);
        Assert.Equal(ShaKey.Project(project.ExternalId), action.OnAck.Key);
    }

    [Fact]
    public void Should_EmitProjectUpdated_Once_Then_Stable_When_IsRemovedFlips()
    {
        var live = DiffTestData.Project(isRemoved: false);
        var map = DiffTestData.MapWith((ShaKey.Project(live.ExternalId), live));

        var removed = DiffTestData.Project(isRemoved: true);
        var input = new DiffInput { Projects = [new ProjectDiffInput(removed.ExternalId, removed)] };

        // First pass: the flag flip changes the hash → one ProjectUpdated.
        var first = DiffTestData.Engine().Diff(input, map);
        var action = Assert.Single(first.Actions);
        Assert.IsType<ProjectUpdated>(action.Event);

        // Apply the ack, then re-diff the same removed payload → no re-storm.
        map.Apply(action.OnAck);
        var second = DiffTestData.Engine().Diff(input, map);
        Assert.Empty(second.Actions);
    }

    [Fact]
    public void Should_NotMutateInputMap_OnDiff()
    {
        var provider = DiffTestData.Provider(name: "New");
        var map = DiffTestData.MapWith((ShaKey.Provider(provider.ExternalId), DiffTestData.Provider(name: "Old")));
        var beforeHash = map.GetHashOrNull(ShaKey.Provider(provider.ExternalId));
        var beforeCount = map.Count;
        var input = new DiffInput { Providers = [new ProviderDiffInput(provider.ExternalId, provider)] };

        _ = DiffTestData.Engine().Diff(input, map);

        Assert.Equal(beforeCount, map.Count);
        Assert.Equal(beforeHash, map.GetHashOrNull(ShaKey.Provider(provider.ExternalId)));
    }
}
