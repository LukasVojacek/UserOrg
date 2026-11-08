using Waldur.Contracts.Events;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Configuration;
using Waldur.Sync.Mapping;
using Waldur.Sync.Tests.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerOfferingSourcingTests
{
    [Fact]
    public async Task Should_SupplyFullOffering_And_CarryHeappeBasePath_When_ResourceIsHpc()
    {
        var heappe = new Dictionary<string, WaldurHeappeInstanceOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [MapperTestData.CustomerId.ToString("N")] = new()
            {
                Uri = new Uri("https://heappe.example.org"),
                Identifier = "cluster-1"
            }
        };
        var scenario = new WorkerTestScenario(heappe: heappe);

        // Full offering (endpoints + components + plugin_options) — the mapper's per-resource source.
        var offering = MapperTestData.Offering(heappeBasePath: "/scratch/projects");
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [MapperTestData.CustomerId] = [offering]
        };
        scenario.Client.Projects = [MapperTestData.Project()];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        var project = Assert.Single(scenario.Publisher.Published.OfType<ProjectCreated>());
        var resource = Assert.Single(project.Payload.Resources);
        Assert.Equal("https://heappe.example.org/", resource.HeappeUri);
        Assert.Equal("cluster-1", resource.HeappeIdentifier);
        Assert.Contains(resource.Values, v =>
            v.Name == MappingConstants.HeappeLocalBasePathKey &&
            v.Value.StartsWith("/scratch/projects/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_SkipResourceAndProject_When_OfferingNotInCustomerFetch()
    {
        var scenario = new WorkerTestScenario();

        // A customer with NO offerings, but a resource referencing an offering id we never fetched.
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [MapperTestData.CustomerId] = []
        };
        scenario.Client.Projects = [MapperTestData.Project()];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource(offeringId: Guid.NewGuid())]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // Offering absent → resource skipped → project ineligible → no ProjectCreated at all.
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProjectCreated);
    }
}
