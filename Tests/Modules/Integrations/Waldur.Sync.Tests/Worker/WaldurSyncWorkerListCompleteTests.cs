using Microsoft.Extensions.Logging;

using Waldur.Contracts.Events;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Tests.Mapping;
using Waldur.Sync.Tests.TestSupport;
using Waldur.Sync.Worker;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

/// <summary>
/// The ListComplete gate (ADR-0064): a partial customer/project LIST fetch (a page failed) still
/// upserts the present items but suppresses that entity type's absence sweep — an item merely on the
/// un-fetched page keeps its breadcrumb and is not re-created next iteration. A complete list still
/// sweeps a genuinely vanished item, so the gate is not over-broad.
/// </summary>
public class WaldurSyncWorkerListCompleteTests
{
    private static readonly Guid CustomerA = MapperTestData.CustomerId;
    private static readonly Guid CustomerB = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OfferingB = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ProjectB = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ResourceB = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private const int FetchDependencyFailedEventId = 5035;

    [Fact]
    public async Task Should_SuppressProviderAbsenceSweep_When_CustomerListIncomplete()
    {
        var scenario = new WorkerTestScenario();
        scenario.Client.Customers = [MapperTestData.Customer(), MapperTestData.Customer(id: CustomerB, name: "Provider B")];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [CustomerA] = [MapperTestData.Offering()],
            [CustomerB] = [MapperTestData.Offering(id: OfferingB, customerId: CustomerB)]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);

        // Iteration 1: both providers created and their breadcrumbs recorded.
        await worker.RunIterationAsync(CancellationToken.None);
        Assert.Equal(2, scenario.Publisher.Published.OfType<ProviderCreated>().Count());

        // Iteration 2: the customer list returns INCOMPLETE with only A (B sits on the failed page).
        scenario.Client.CustomerListIncomplete = true;
        scenario.Client.Customers = [MapperTestData.Customer(name: "Provider A v2")];

        var s2 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.False(s2.FetchFailed);                                          // iteration completes (no whole-phase abort)
        Assert.Equal(0, s2.Absent);                                           // gate suppresses the provider sweep — B not absent
        Assert.Contains(scenario.Publisher.Published, e =>
            e is ProviderUpdated p && p.Payload.ExternalId == CustomerA.ToString("D")); // present item still upserts

        // Iteration 3: list complete again with both present. B's breadcrumb survived → no re-create.
        scenario.Client.CustomerListIncomplete = false;
        scenario.Client.Customers = [MapperTestData.Customer(name: "Provider A v2"), MapperTestData.Customer(id: CustomerB, name: "Provider B")];

        var s3 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(0, s3.Created);
        Assert.Equal(0, s3.Absent);
    }

    [Fact]
    public async Task Should_SuppressProjectAbsenceSweep_When_ProjectListIncomplete()
    {
        var scenario = new WorkerTestScenario();
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [CustomerA] = [MapperTestData.Offering()]
        };
        scenario.Client.Projects = [MapperTestData.Project(), MapperTestData.Project(id: ProjectB, slug: "project-b")];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()],
            [ProjectB] = [MapperTestData.Resource(id: ResourceB)]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);

        // Iteration 1: both projects created and their breadcrumbs recorded.
        await worker.RunIterationAsync(CancellationToken.None);
        Assert.Equal(2, scenario.Publisher.Published.OfType<ProjectCreated>().Count());

        // Iteration 2: the project list returns INCOMPLETE with only A (B sits on the failed page).
        scenario.Client.ProjectListIncomplete = true;
        scenario.Client.Projects = [MapperTestData.Project(name: "My Project v2")];

        var s2 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.False(s2.FetchFailed);                                         // iteration completes
        Assert.Equal(0, s2.Absent);                                          // gate suppresses the project sweep — B not absent
        Assert.Contains(scenario.Publisher.Published, e =>
            e is ProjectUpdated p && p.Payload.ExternalId == MapperTestData.ProjectId.ToString("D")); // present item still upserts

        // Iteration 3: list complete again with both present. B's breadcrumb survived → no re-create.
        scenario.Client.ProjectListIncomplete = false;
        scenario.Client.Projects = [MapperTestData.Project(name: "My Project v2"), MapperTestData.Project(id: ProjectB, slug: "project-b")];

        var s3 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(0, s3.Created);
        Assert.Equal(0, s3.Absent);
    }

    [Fact]
    public async Task Should_StillSweepAbsence_When_ListComplete()
    {
        // Control: the gate is not over-broad — a COMPLETE list that genuinely lost an item still sweeps it.
        var scenario = new WorkerTestScenario();
        scenario.Client.Customers = [MapperTestData.Customer(), MapperTestData.Customer(id: CustomerB, name: "Provider B")];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [CustomerA] = [MapperTestData.Offering()],
            [CustomerB] = [MapperTestData.Offering(id: OfferingB, customerId: CustomerB)]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // Iteration 2: B genuinely vanishes from a COMPLETE list (CustomerListIncomplete stays false).
        scenario.Client.Customers = [MapperTestData.Customer()];

        var s2 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(1, s2.Absent); // B swept to absence — the complete list is authoritative
    }

    [Fact]
    public async Task Should_LogFetchDependencyFailedMarker_When_CustomerAndProjectListIncomplete()
    {
        var logger = new CapturingLogger<WaldurSyncWorker>();
        var scenario = new WorkerTestScenario { Logger = logger };
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [CustomerA] = [MapperTestData.Offering()]
        };
        scenario.Client.Projects = [MapperTestData.Project()];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()]
        };
        scenario.Client.CustomerListIncomplete = true;
        scenario.Client.ProjectListIncomplete = true;

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // ADR-0064/0066: each incomplete LIST fetch logs the dedicated Error marker keyed by its list id.
        Assert.Contains(logger.Entries, e =>
            e.EventId.Id == FetchDependencyFailedEventId && e.Level == LogLevel.Error
            && e.ScopeValue("ExternalId") == "customer-list");
        Assert.Contains(logger.Entries, e =>
            e.EventId.Id == FetchDependencyFailedEventId && e.Level == LogLevel.Error
            && e.ScopeValue("ExternalId") == "project-list");
    }
}
