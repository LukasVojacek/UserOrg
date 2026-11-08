using System.Reflection;

using Core.Domain.Shared;

using DomainProvider = Core.Domain.Provider.Provider;
using DomainProviderName = Core.Domain.Provider.ProviderName;
using DomainLocationName = Core.Domain.Provider.LocationName;
using DomainResource = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Providers;

/// <summary>
/// Invariant tests for the resource-assignment write surface (ADR-0016 / ADR-0041) — directly on the
/// aggregate, no mocks. AssignResource now takes the resolved <see cref="DomainResource"/> entity
/// (ADR-0041 L2.2). Invariant #1 (resource already has a non-shared location) arrives as a
/// use-case-supplied flag; invariant #2 (non-shared location holds one active assignment), invariant #5
/// (resource belongs to this provider, L2.3) and the read-only guards are enforced in the aggregate.
/// </summary>
public sealed class ResourceAssignmentDomainTests
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DomainLocationName Gpu = DomainLocationName.From("gpu");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    // v2 allocations are addressed by their slug (ADR-0024); the use-case mints it and passes it into the
    // aggregate, so these direct-on-aggregate tests supply one explicitly and address update/remove by it.
    private const string Slug = "asg-gpu-abc123";

    private static DomainProvider CreateProviderWithLocation(bool isShared = true)
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        Assert.True(provider.AddLocation(Gpu, "https://gpu", ELocationType.HPC, isShared: isShared).IsSuccess);
        return provider;
    }

    // A resource OWNED by the given provider (its ProviderName matches), so invariant #5 passes.
    private static DomainResource ResourceOf(DomainProvider provider, string name = "res", EOperationMode mode = EOperationMode.Unspecified) =>
        DomainResource.Create(ResourceName.Create(name), provider, UserId.From(Guid.NewGuid()), Start, End, operationMode: mode);

    [Fact]
    public void Should_NotCreateInitialAssignedLocation_When_AddLocation()
    {
        var provider = CreateProviderWithLocation();

        Assert.Empty(provider.Locations.Single().AssignedLocations);
    }

    [Fact]
    public void Should_CreateAssignmentWithSpecifications_When_AssignResource()
    {
        var provider = CreateProviderWithLocation();
        var resource = ResourceOf(provider, "gpu-res");

        var result = provider.AssignResource(
            Gpu, resource, allocationUnitTypeId: 2, allocationUnitCount: 100,
            description: "alloc", aggregationName: "agg",
            specifications: [("cpu", "16"), ("cpu", "32")],
            resourceHasNonSharedLocation: false);

        Assert.True(result.IsSuccess);
        var assignment = Assert.Single(provider.Locations.Single().AssignedLocations);
        Assert.Equal(resource.Key, assignment.ResourceKey);
        Assert.Equal(2, assignment.AllocationUnitTypeId);
        Assert.Equal(100, assignment.AllocationUnitCount);
        Assert.NotEqual(Guid.Empty, assignment.Id);
        // Duplicate spec names are allowed (legacy parity — no uniqueness on specifications).
        Assert.Equal(2, assignment.Values.Count);
        Assert.All(assignment.Values, v => Assert.Equal("cpu", v.Name));
    }

    [Fact]
    public void Should_RaiseResourceAssignmentChangedEvent_When_AssignUpdateAndRemove()
    {
        var provider = CreateProviderWithLocation();
        var resource = ResourceOf(provider, "gpu-res");

        var assignment = provider.AssignResource(Gpu, resource, 2, null, null, null, NoSpecs, resourceHasNonSharedLocation: false, slug: Slug).Value;
        Assert.True(provider.UpdateAssignment(Gpu, assignment.Slug!, 3, 5, null, null, NoSpecs).IsSuccess);
        Assert.True(provider.RemoveAssignment(Gpu, assignment.Slug!).IsSuccess);

        var events = provider.DomainEvents.OfType<ResourceAssignmentChangedEvent>().ToList();
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(resource.Key, e.Resource));
    }

    [Fact]
    public void Should_RejectAssignResource_When_ResourceUnderDifferentProvider()
    {
        // Invariant #5 (ADR-0041 L2.3): a resource owned by another provider cannot be assigned here.
        var provider = CreateProviderWithLocation();
        var otherProvider = DomainProvider.Create(DomainProviderName.From("beta"), countryId: 1);
        var foreignResource = ResourceOf(otherProvider, "foreign-res");

        var result = provider.AssignResource(Gpu, foreignResource, 2, null, null, null, NoSpecs, resourceHasNonSharedLocation: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.ResourceNotUnderProvider, result.Error.Code);
        Assert.Empty(provider.DomainEvents.OfType<ResourceAssignmentChangedEvent>());
        Assert.Empty(provider.Locations.Single().AssignedLocations);
    }

    [Fact]
    public void Should_RejectAssignResource_When_ResourceHasNullProvider()
    {
        // Invariant #5 against a legacy provider-less resource (ProviderName null): the guard compares
        // resource.ProviderName != Name → null != "acme" is true → ResourceNotUnderProvider, SAFELY. The error
        // is built from ResourceName (not the throwing Key getter), so no NullReferenceException, no allocation,
        // no event. ProjectResource.Create requires a non-null provider, so null out the _provider backing field
        // (the same minimal reflection seam the suite uses for private state) to forge the legacy row.
        var provider = CreateProviderWithLocation();
        var orphanResource = ResourceOf(provider, "legacy-res");
        typeof(DomainResource).GetField("_provider", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(orphanResource, null);

        var result = provider.AssignResource(Gpu, orphanResource, 2, null, null, null, NoSpecs, resourceHasNonSharedLocation: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.ResourceNotUnderProvider, result.Error.Code);
        Assert.Empty(provider.DomainEvents.OfType<ResourceAssignmentChangedEvent>());
        Assert.Empty(provider.Locations.Single().AssignedLocations);
    }

    [Fact]
    public void Should_RejectAssignResource_When_ResourceAlreadyHasNonSharedLocation()
    {
        var provider = CreateProviderWithLocation();

        var result = provider.AssignResource(Gpu, ResourceOf(provider), 2, null, null, null, NoSpecs, resourceHasNonSharedLocation: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.ResourceAlreadyHasNonSharedLocation, result.Error.Code);
        Assert.Empty(provider.DomainEvents.OfType<ResourceAssignmentChangedEvent>());
    }

    [Fact]
    public void Should_RejectAssignResource_When_NonSharedLocationAlreadyHasActiveResource()
    {
        var provider = CreateProviderWithLocation(isShared: false);
        Assert.True(provider.AssignResource(Gpu, ResourceOf(provider, "r1"), 2, null, null, null, NoSpecs, false).IsSuccess);

        var second = provider.AssignResource(Gpu, ResourceOf(provider, "r2"), 2, null, null, null, NoSpecs, false);

        Assert.False(second.IsSuccess);
        Assert.Equal(ProviderErrorCodes.NonSharedLocationAlreadyHasResource, second.Error.Code);
    }

    [Fact]
    public void Should_AllowMultipleAssignments_When_LocationIsShared()
    {
        var provider = CreateProviderWithLocation(isShared: true);
        var resource = ResourceOf(provider, "gpu-res");

        // Same resource twice on one shared location — the (LocationId, ProjectResourceId)
        // composite is non-unique by design (ADR-0016).
        Assert.True(provider.AssignResource(Gpu, resource, 2, null, null, null, NoSpecs, false).IsSuccess);
        Assert.True(provider.AssignResource(Gpu, resource, 3, null, null, "other", NoSpecs, false).IsSuccess);

        Assert.Equal(2, provider.Locations.Single().AssignedLocations.Count);
    }

    [Fact]
    public void Should_AllowAssignResource_When_NonSharedLocationHasOnlyDeletedAssignments()
    {
        var provider = CreateProviderWithLocation(isShared: false);
        var first = provider.AssignResource(Gpu, ResourceOf(provider, "r1"), 2, null, null, null, NoSpecs, false, slug: Slug).Value;
        Assert.True(provider.RemoveAssignment(Gpu, first.Slug!).IsSuccess);

        var second = provider.AssignResource(Gpu, ResourceOf(provider, "r2"), 2, null, null, null, NoSpecs, false);

        Assert.True(second.IsSuccess);
    }

    [Fact]
    public void Should_RejectAssignResource_When_LocationReadOnly()
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        var location = Location.Create(Gpu, "https://gpu", ELocationType.HPC, mode: EOperationMode.ReadOnly);

        var result = location.AssignResource(ResourceOf(provider), 2, null, null, null, NoSpecs, false);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.LocationReadOnly, result.Error.Code);
    }

    [Fact]
    public void Should_RejectUpdateAndRemove_When_AssignmentReadOnly()
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        var location = Location.Create(Gpu, "https://gpu", ELocationType.HPC);
        var assignment = location.AssignResource(ResourceOf(provider), 2, null, null, null, NoSpecs, false, mode: EOperationMode.ReadOnly, slug: Slug).Value;

        var update = location.UpdateAssignment(assignment.Slug!, 3, null, null, null, NoSpecs);
        var remove = location.RemoveAssignment(assignment.Slug!);

        Assert.Equal(ProviderErrorCodes.AssignmentReadOnly, update.Error.Code);
        Assert.Equal(ProviderErrorCodes.AssignmentReadOnly, remove.Error.Code);
    }

    [Fact]
    public void Should_ReplaceAllSpecificationsAndDetails_When_UpdateAssignment()
    {
        var provider = CreateProviderWithLocation();
        var assignment = provider.AssignResource(
            Gpu, ResourceOf(provider), 2, 10, "old", "oldAgg",
            [("cpu", "16")], false, slug: Slug).Value;

        var result = provider.UpdateAssignment(Gpu, assignment.Slug!, 3, 20, "new", "newAgg", [("ram", "64"), ("disk", "1")]);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, assignment.AllocationUnitTypeId);
        Assert.Equal(20, assignment.AllocationUnitCount);
        Assert.Equal("new", assignment.Description);
        Assert.Equal("newAgg", assignment.AggregationName);
        // Replace-all semantics: the previous set is gone, only the supplied set remains.
        Assert.Equal(["ram", "disk"], assignment.Values.Select(v => v.Name));
    }

    [Fact]
    public void Should_KeepDuplicateSpecificationNames_When_UpdateAssignment()
    {
        var provider = CreateProviderWithLocation();
        var assignment = provider.AssignResource(
            Gpu, ResourceOf(provider), 2, 10, null, null,
            [("cpu", "16")], false, slug: Slug).Value;

        var result = provider.UpdateAssignment(Gpu, assignment.Slug!, 2, 10, null, null, [("cpu", "8"), ("cpu", "32")]);

        Assert.True(result.IsSuccess);
        Assert.Equal([("cpu", "8"), ("cpu", "32")], assignment.Values.Select(v => (v.Name, v.Value)));
    }

    [Fact]
    public void Should_SoftDeleteAssignment_When_RemoveAssignment()
    {
        var provider = CreateProviderWithLocation();
        var assignment = provider.AssignResource(Gpu, ResourceOf(provider), 2, null, null, null, NoSpecs, false, slug: Slug).Value;

        Assert.True(provider.RemoveAssignment(Gpu, assignment.Slug!).IsSuccess);

        Assert.True(assignment.IsDeleted);
        var secondRemove = provider.RemoveAssignment(Gpu, assignment.Slug!);
        var updateAfterRemove = provider.UpdateAssignment(Gpu, assignment.Slug!, 3, null, null, null, NoSpecs);
        Assert.Equal(ProviderErrorCodes.AssignedLocationAlreadyDeleted, secondRemove.Error.Code);
        Assert.Equal(ProviderErrorCodes.AssignedLocationAlreadyDeleted, updateAfterRemove.Error.Code);
    }

    [Fact]
    public void Should_RejectAssignment_When_AssignmentNotFound()
    {
        var provider = CreateProviderWithLocation();

        var result = provider.UpdateAssignment(Gpu, "asg-nope-000000", 2, null, null, null, NoSpecs);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.AssignedLocationNotFound, result.Error.Code);
    }

    [Fact]
    public void Should_RejectAssignment_When_LocationNotFoundOrDeleted()
    {
        var provider = CreateProviderWithLocation();
        var unknown = provider.AssignResource(DomainLocationName.From("cpu"), ResourceOf(provider), 2, null, null, null, NoSpecs, false);
        Assert.Equal(ProviderErrorCodes.LocationNotFound, unknown.Error.Code);

        Assert.True(provider.RemoveLocation(Gpu).IsSuccess);
        var deleted = provider.AssignResource(Gpu, ResourceOf(provider), 2, null, null, null, NoSpecs, false);
        Assert.Equal(ProviderErrorCodes.LocationAlreadyDeleted, deleted.Error.Code);
    }
}
