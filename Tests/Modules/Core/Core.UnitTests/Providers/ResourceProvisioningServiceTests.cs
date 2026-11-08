using System.Reflection;

using Core.Domain.Shared;

using DomainProvider = Core.Domain.Provider.Provider;
using DomainProviderName = Core.Domain.Provider.ProviderName;
using DomainLocationName = Core.Domain.Provider.LocationName;

namespace Core.UnitTests.Providers;

/// <summary>
/// Unit tests for the pure <see cref="ResourceProvisioningService"/> (ADR-0040) — no mocks, a real
/// <see cref="DomainProvider"/> aggregate with a real <see cref="Location"/>. The service creates a
/// <see cref="ProjectResource"/> and allocates it via <c>provider.AssignResource(entity)</c> in one in-memory
/// operation, returning the resource on success and only domain errors on failure.
/// </summary>
public sealed class ResourceProvisioningServiceTests
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DomainLocationName Gpu = DomainLocationName.From("gpu");
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private const string Slug = "asg-gpu-abc123";

    private static readonly IResourceProvisioningService Service = new ResourceProvisioningService();

    private static readonly DomainLocationName Cpu = DomainLocationName.From("cpu");

    private static DomainProvider ProviderWithLocation(bool isShared = true)
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        Assert.True(provider.AddLocation(Gpu, "https://gpu", ELocationType.HPC, isShared: isShared).IsSuccess);
        return provider;
    }

    private static DomainProvider ProviderWithTwoLocations(bool gpuShared = true, bool cpuShared = true)
    {
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        Assert.True(provider.AddLocation(Gpu, "https://gpu", ELocationType.HPC, isShared: gpuShared).IsSuccess);
        Assert.True(provider.AddLocation(Cpu, "https://cpu", ELocationType.HPC, isShared: cpuShared).IsSuccess);
        return provider;
    }

    private static NewResourceSpec NewResource(string name = "gpu-res") =>
        new(ResourceName.Create(name), Start, End, Description: null, PrimaryInvestigatorEmail: null, LicenseUrl: null);

    private static ResourceAllocationSpec Allocation(string slug = Slug) =>
        new(AllocationUnitTypeId: 2, AllocationUnitCount: 100, Description: "alloc",
            AggregationName: "agg", Specifications: NoSpecs, Slug: slug);

    private static LocationAssignmentSpec Assignment(DomainLocationName location, string slug = Slug) =>
        new(location, Allocation(slug));

    [Fact]
    public void Should_CreateResourceAndAllocate_When_Provision()
    {
        var provider = ProviderWithLocation();

        var result = Service.Provision(provider, NewResource(), UserId.Create(Guid.NewGuid()), [Assignment(Gpu)]);

        Assert.True(result.IsSuccess);
        var resource = result.Value;
        Assert.Equal(ResourceName.Create("gpu-res"), resource.ResourceName);

        var assignment = Assert.Single(provider.Locations.Single().AssignedLocations);
        // The allocation's resource navigation is wired to the freshly created resource.
        Assert.Equal(resource.Key, assignment.ResourceKey);
        Assert.Equal(resource.ResourceName.Value, assignment.ResourceKey?.Name.Value);
        Assert.Equal(2, assignment.AllocationUnitTypeId);
        Assert.Equal(Slug, assignment.Slug);
    }

    [Fact]
    public void Should_RaiseExactlyOneResourceAssignmentChangedEvent_When_Provision()
    {
        var provider = ProviderWithLocation();

        var result = Service.Provision(provider, NewResource(), UserId.Create(Guid.NewGuid()), [Assignment(Gpu)]);

        var events = provider.DomainEvents.OfType<ResourceAssignmentChangedEvent>().ToList();
        var raised = Assert.Single(events);
        Assert.Equal(result.Value.Key, raised.Resource);
    }

    [Fact]
    public void Should_FailWithoutResourceOrEvent_When_LocationReadOnly()
    {
        // The service surfaces the read-only guard the aggregate delegates to its location. A read-only
        // location is the domain-enforced read-only path; the provider-level read-only check is an endpoint
        // guard over the resource operation mode, covered separately at the endpoint.
        var provider = DomainProvider.Create(DomainProviderName.From("acme"), countryId: 1);
        AddReadOnlyLocation(provider, Gpu, "https://gpu");

        var result = Service.Provision(provider, NewResource(), UserId.Create(Guid.NewGuid()), [Assignment(Gpu)]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.LocationReadOnly, result.Error.Code);
        Assert.Empty(provider.Locations.Single().AssignedLocations);
        Assert.Empty(provider.DomainEvents.OfType<ResourceAssignmentChangedEvent>());
    }

    [Fact]
    public void Should_FailWithLocationNotFound_When_UnknownLocation()
    {
        var provider = ProviderWithLocation();

        var result = Service.Provision(provider, NewResource(), UserId.Create(Guid.NewGuid()), [Assignment(DomainLocationName.From("cpu"))]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.LocationNotFound, result.Error.Code);
        Assert.Empty(provider.DomainEvents.OfType<ResourceAssignmentChangedEvent>());
    }

    [Fact]
    public void Should_FailWithLocalInvariant_When_NonSharedLocationAlreadyAllocated()
    {
        var provider = ProviderWithLocation(isShared: false);
        Assert.True(Service.Provision(provider, NewResource("r1"), UserId.Create(Guid.NewGuid()), [Assignment(Gpu, "asg-gpu-r1")]).IsSuccess);

        var second = Service.Provision(provider, NewResource("r2"), UserId.Create(Guid.NewGuid()), [Assignment(Gpu, "asg-gpu-r2")]);

        Assert.False(second.IsSuccess);
        Assert.Equal(ProviderErrorCodes.NonSharedLocationAlreadyHasResource, second.Error.Code);
    }

    [Fact]
    public void Should_AssignToEveryLocation_When_ProvisionWithMultipleLocations()
    {
        var provider = ProviderWithTwoLocations();

        var result = Service.Provision(
            provider,
            NewResource(),
            UserId.Create(Guid.NewGuid()),
            [Assignment(Gpu, "asg-gpu-1"), Assignment(Cpu, "asg-cpu-1")]);

        Assert.True(result.IsSuccess);
        Assert.Single(provider.Locations.Single(l => l.Name.Equals(Gpu)).AssignedLocations);
        Assert.Single(provider.Locations.Single(l => l.Name.Equals(Cpu)).AssignedLocations);
    }

    [Fact]
    public void Should_FailSecondEntry_When_TwoNonSharedLocationsInSameBatch()
    {
        // Same resource, brand new — the only way it can hit "already has a non-shared location" is against
        // an earlier entry in this same batch (ADR-0050 §2), since the DB has never heard of this resource.
        var provider = ProviderWithTwoLocations(gpuShared: false, cpuShared: false);

        var result = Service.Provision(
            provider,
            NewResource(),
            UserId.Create(Guid.NewGuid()),
            [Assignment(Gpu, "asg-gpu-1"), Assignment(Cpu, "asg-cpu-1")]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.ResourceAlreadyHasNonSharedLocation, result.Error.Code);
    }

    [Fact]
    public void Should_AbortWholeBatch_When_OneEntryFails()
    {
        var provider = ProviderWithLocation();

        var result = Service.Provision(
            provider,
            NewResource(),
            UserId.Create(Guid.NewGuid()),
            [Assignment(Gpu, "asg-gpu-1"), Assignment(DomainLocationName.From("cpu"), "asg-cpu-1")]);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.LocationNotFound, result.Error.Code);
        // The first entry succeeded in-memory against the aggregate, but nothing overall is returned as
        // success — the application only persists on Provision's own success, so this stays unpersisted.
        Assert.Single(provider.Locations.Single().AssignedLocations);
    }

    [Fact]
    public void Should_FailWithNoLocationAssignments_When_AssignmentsEmpty()
    {
        // A resource with no location has no use case (ADR-0050) — enforced in the service itself, not just
        // at the endpoint's input validation, so no other caller can bypass it.
        var provider = ProviderWithLocation();

        var result = Service.Provision(provider, NewResource(), UserId.Create(Guid.NewGuid()), []);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.NoLocationAssignments, result.Error.Code);
        Assert.Empty(provider.Locations.Single().AssignedLocations);
    }

    // No public API sets a location's mode, so inject a read-only location into the provider's backing
    // collection to exercise the read-only guard the service surfaces. Mirrors Location.Create's mode hook.
    private static void AddReadOnlyLocation(DomainProvider provider, DomainLocationName name, string endpoint)
    {
        var location = Location.Create(name, endpoint, ELocationType.HPC, mode: EOperationMode.ReadOnly);
        var field = typeof(DomainProvider).GetField("_providerLocations", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var locations = (ICollection<Location>)field.GetValue(provider)!;
        locations.Add(location);
    }
}
