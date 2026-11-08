using Microsoft.Extensions.Logging;

using Waldur.Contracts.Events;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Tests.Mapping;
using Waldur.Sync.Tests.TestSupport;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

/// <summary>
/// Per-item detail-fetch isolation (ADR-0062): a single failed offering/resource fetch is carried
/// forward (present, null payload) instead of aborting the whole iteration, so healthy siblings still
/// emit and the failed item's breadcrumb survives (no spurious re-create next iteration).
/// </summary>
public class WaldurSyncWorkerFetchIsolationTests
{
    private static readonly Guid CustomerA = MapperTestData.CustomerId;
    private static readonly Guid CustomerB = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OfferingB = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task Should_CarryForwardProvider_When_OfferingFetchFails()
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

        // Iteration 2: B's offering fetch fails; A is changed so a healthy sibling still emits.
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerB);
        scenario.Client.Customers = [MapperTestData.Customer(name: "Provider A v2"), MapperTestData.Customer(id: CustomerB, name: "Provider B")];

        var s2 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.False(s2.FetchFailed);                                          // iteration completes (no whole-phase abort)
        Assert.Equal(0, s2.Absent);                                           // B carried forward, not an absence
        Assert.Contains(scenario.Publisher.Published, e =>
            e is ProviderUpdated p && p.Payload.ExternalId == CustomerA.ToString("D")); // healthy sibling still emits

        // Iteration 3: B recovers unchanged. Its breadcrumb survived the failure, so no re-create.
        scenario.Client.CustomersThatFailOfferingFetch.Clear();

        var s3 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(0, s3.Created);
        Assert.Equal(0, s3.Absent);
    }

    private static readonly Guid ProjectB = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ResourceB = Guid.Parse("88888888-8888-8888-8888-888888888888");

    [Fact]
    public async Task Should_CarryForwardProject_When_ResourceFetchFails()
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
        scenario.Client.UsersByProject = new Dictionary<Guid, IReadOnlyList<ApiProjectUser>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.User("alice")],
            [ProjectB] = [MapperTestData.User("carol")]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);

        // Iteration 1: both projects created; carol recorded as a member of project B.
        await worker.RunIterationAsync(CancellationToken.None);
        Assert.Equal(2, scenario.Publisher.Published.OfType<ProjectCreated>().Count());

        // Iteration 2: project B's resource fetch fails, and a new member (dave) joins it.
        scenario.Client.ProjectsThatFailResourceFetch.Add(ProjectB);
        scenario.Client.UsersByProject = new Dictionary<Guid, IReadOnlyList<ApiProjectUser>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.User("alice")],
            [ProjectB] = [MapperTestData.User("carol"), MapperTestData.User("dave")]
        };

        var s2 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.False(s2.FetchFailed);                                        // iteration completes (no whole-phase abort)
        Assert.Equal(0, s2.Absent);                                          // B carried forward, not an absence
        Assert.DoesNotContain(scenario.Publisher.Published, e =>
            e is ProjectUpdated p && p.Payload.ExternalId == ProjectB.ToString("D")); // null payload → no project event
        Assert.Contains(scenario.Publisher.Published, e =>
            e is UserAssigned u && u.Payload.ProjectExternalId == ProjectB.ToString("D")
            && u.Payload.UserName == "dave");                                // D12: membership still fetched + emitted

        // Iteration 3: B's resource fetch recovers unchanged → breadcrumb survived → no re-create.
        scenario.Client.ProjectsThatFailResourceFetch.Clear();

        var s3 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(0, s3.Created);
        Assert.Equal(0, s3.Absent);
    }

    private const int ResourceSkippedByFetchFailureEventId = 5033;

    [Fact]
    public async Task Should_LogDebug_When_ResourceSkippedByFetchFailure()
    {
        var logger = new CapturingLogger<Waldur.Sync.Worker.WaldurSyncWorker>();
        var scenario = new WorkerTestScenario { Logger = logger };

        // The customer's offering fetch fails, so its offering never lands in offeringsById; the
        // project's resource (fetched fine) references that now-missing offering.
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerA);
        scenario.Client.Projects = [MapperTestData.Project()];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        // The dependent resource skip is logged at Debug, distinct from a genuine ResourceOfferingMissing.
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Debug && e.EventId.Id == ResourceSkippedByFetchFailureEventId);

        // The project is left with no eligible resources and is carried forward (no create, iteration completes).
        Assert.False(summary.FetchFailed);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProjectCreated);
    }

    [Fact]
    public async Task Should_EmitMembership_When_ProjectCarriedForwardByOfferingFailure()
    {
        // D12/ADR-0063: a project emptied to a null payload because its sole resource's offering was
        // dropped when that offering's CUSTOMER fetch failed (offering→customer skip) is a transient
        // carry-forward — resourceFetchFailed == false, payload == null. Its membership must STILL emit.
        var scenario = new WorkerTestScenario();
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [CustomerA] = [MapperTestData.Offering()]
        };
        scenario.Client.Projects = [MapperTestData.Project()];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()] // references the now-missing offering
        };
        scenario.Client.UsersByProject = new Dictionary<Guid, IReadOnlyList<ApiProjectUser>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.User("alice")]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);

        // Iteration 1: CustomerA's offering fetch fails, so the project maps to a null payload
        // (ProjectNoEligibleResources) with resourceFetchFailed == false.
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerA);

        var s1 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.False(s1.FetchFailed);
        Assert.Equal(0, s1.Absent);
        // Null payload → no project event this iteration …
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProjectCreated);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProjectUpdated);
        // … but the offering-failure carry-forward still emits membership (D12).
        Assert.Contains(scenario.Publisher.Published, e =>
            e is UserAssigned u && u.Payload.ProjectExternalId == MapperTestData.ProjectId.ToString("D")
            && u.Payload.UserName == "alice");

        // Iteration 2: the offering fetch recovers. The member breadcrumb survived, so the now-created
        // project does not re-emit alice's assignment (no re-create on recovery).
        scenario.Client.CustomersThatFailOfferingFetch.Clear();

        await worker.RunIterationAsync(CancellationToken.None);

        Assert.Single(scenario.Publisher.Published, e => e is UserAssigned);   // not re-created
        Assert.Contains(scenario.Publisher.Published, e => e is ProjectCreated); // project appears now
    }

    [Fact]
    public async Task Should_CarryForwardProject_And_NoMembership_When_BothResourceAndUserFetchFail()
    {
        var scenario = new WorkerTestScenario();
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
        scenario.Client.UsersByProject = new Dictionary<Guid, IReadOnlyList<ApiProjectUser>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.User("alice")]
        };

        var worker = scenario.Build();
        await worker.StartupAsync(CancellationToken.None);

        // Iteration 1: project created, alice recorded as a member.
        await worker.RunIterationAsync(CancellationToken.None);
        Assert.Contains(scenario.Publisher.Published, e => e is ProjectCreated);

        // Iteration 2: the SAME project fails BOTH its resource fetch and its user fetch.
        scenario.Client.ProjectsThatFailResourceFetch.Add(MapperTestData.ProjectId);
        scenario.Client.ProjectsThatFailUserFetch.Add(MapperTestData.ProjectId);

        var s2 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.False(s2.FetchFailed);                                         // iteration completes
        Assert.Equal(0, s2.Absent);                                          // carried forward, not an absence
        Assert.DoesNotContain(scenario.Publisher.Published, e =>
            e is ProjectUpdated p && p.Payload.ExternalId == MapperTestData.ProjectId.ToString("D")); // null payload → no project event
        // MembershipSnapshot.Failed → no member events (alice neither removed nor re-assigned).
        Assert.Single(scenario.Publisher.Published, e => e is UserAssigned);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is UserRemoved);

        // Iteration 3: both fetches recover unchanged → breadcrumb survived → no re-create.
        scenario.Client.ProjectsThatFailResourceFetch.Clear();
        scenario.Client.ProjectsThatFailUserFetch.Clear();

        var s3 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(0, s3.Created);
        Assert.Equal(0, s3.Absent);
    }

    [Fact]
    public async Task Should_CarryForwardAll_When_AllOfferingFetchesFail()
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

        // Iteration 2: EVERY offering fetch throws — a total upstream outage.
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerA);
        scenario.Client.CustomersThatFailOfferingFetch.Add(CustomerB);

        var s2 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.False(s2.FetchFailed);   // iteration completes, no crash
        Assert.Equal(0, s2.Absent);     // every provider carried forward, nothing swept
        Assert.Equal(0, s2.Created);
        Assert.Equal(0, s2.Removed);

        // Iteration 3: the outage clears; all breadcrumbs survived → no re-create.
        scenario.Client.CustomersThatFailOfferingFetch.Clear();

        var s3 = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(0, s3.Created);
        Assert.Equal(0, s3.Absent);
    }
}
