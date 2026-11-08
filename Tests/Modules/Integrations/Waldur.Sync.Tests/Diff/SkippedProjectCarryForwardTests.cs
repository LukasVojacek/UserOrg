using Waldur.Contracts.Events;
using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Diff;
using Waldur.Sync.Tests.Mapping;
using Waldur.Sync.Tests.Worker;

using Xunit;

namespace Waldur.Sync.Tests.Diff;

/// <summary>
/// Pins the safety property the mapper's slug-skip (D4/D5) depends on: a project whose slug fails
/// <c>IsValidShortName</c> produces no payload, but must never be treated as ABSENT — that would sweep
/// its breadcrumb and its members instead of merely leaving it un-upserted. Runs the real
/// <see cref="Waldur.Sync.Worker.WaldurSyncWorker"/> over the real <see cref="Waldur.Sync.Mapping.WaldurMapper"/>
/// and <see cref="DiffEngine"/> (no hand-built <see cref="DiffInput"/>) so a regression in the worker's
/// carry-forward plumbing — not just the diff engine's own rules — turns these tests red.
/// Scope of the membership half: it holds for an iteration with no offering/resource fetch failure.
/// Under such a failure <c>FetchMembershipAsync</c> fetches members for EVERY non-removed project by
/// iteration-granularity attribution (D12/ADR-0063), a slug-skipped one included.
/// </summary>
public sealed class SkippedProjectCarryForwardTests
{
    private const string SeededMemberUserName = "alice";
    private static readonly string CustomerExternalId = MapperTestData.CustomerId.ToString("D");
    private static readonly string ProjectExternalId = MapperTestData.ProjectId.ToString("D");

    /// <summary>
    /// A full happy path — customer, offering and an eligible resource on that offering — whose single
    /// project carries a slug that is now invalid (space and <c>!</c> are outside <c>[A-Za-z0-9_-]</c>).
    /// The invalid slug is therefore the ONLY reason this iteration produces no project payload: with a
    /// valid slug the very same scenario maps, upserts and fetches members. The seed map is what a prior
    /// successful sync left behind (provider + project + one member SHA), and Waldur no longer reports
    /// that member — so a membership sweep would emit <see cref="UserRemoved"/> and drop its SHA.
    /// </summary>
    private static WorkerTestScenario BuildScenarioWithInvalidSlugAndExistingBreadcrumb()
    {
        var scenario = new WorkerTestScenario
        {
            Rebuilder = new FakeShaMapRebuilder(new Dictionary<ShaKey, string>
            {
                [ShaKey.Provider(CustomerExternalId)] = "seed-provider-hash",
                [ShaKey.Project(ProjectExternalId)] = "seed-project-hash",
                [ShaKey.Member(ProjectExternalId, SeededMemberUserName)] = "seed-member-hash"
            })
        };
        scenario.Client.Customers = [MapperTestData.Customer()];
        scenario.Client.OfferingsByCustomer = new Dictionary<Guid, IReadOnlyList<ApiPublicOffering>>
        {
            [MapperTestData.CustomerId] = [MapperTestData.Offering()]
        };
        scenario.Client.Projects = [MapperTestData.Project(slug: "invalid slug!")];
        scenario.Client.ResourcesByProject = new Dictionary<Guid, IReadOnlyList<ApiMarketplaceResource>>
        {
            [MapperTestData.ProjectId] = [MapperTestData.Resource()]
        };
        return scenario;
    }

    [Fact]
    public async Task Should_KeepBreadcrumb_When_ProjectSlugIsInvalid()
    {
        var scenario = BuildScenarioWithInvalidSlugAndExistingBreadcrumb();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // 3 = the seeded provider + project + member SHAs, all still there: "skipped" carries the
        // project forward with its member, it does not drop either from the map.
        Assert.Equal(3, worker.MapSize);
    }

    [Fact]
    public async Task Should_NotUpsert_And_NotReportAbsent_When_ProjectSlugIsInvalid()
    {
        var scenario = BuildScenarioWithInvalidSlugAndExistingBreadcrumb();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        var summary = await worker.RunIterationAsync(CancellationToken.None);

        // The absence of an upsert event is what makes the invalid slug — not a missing resource —
        // the operative cause: with a valid slug this scenario emits ProjectUpdated.
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is ProjectCreated or ProjectUpdated);
        Assert.Equal(0, summary.Absent);
    }

    [Fact]
    public async Task Should_NotFetchOrSweepMembers_When_ProjectSlugIsInvalid_AndNoFetchFailure()
    {
        var scenario = BuildScenarioWithInvalidSlugAndExistingBreadcrumb();
        var worker = scenario.Build();

        await worker.StartupAsync(CancellationToken.None);
        await worker.RunIterationAsync(CancellationToken.None);

        // The call count is load-bearing, not redundant with the assertion below: the mechanism is a
        // NULL MembershipSnapshot (members never fetched), and a fetch returning exactly the seeded
        // members would emit no UserRemoved either — masking a change of mechanism.
        Assert.Equal(0, scenario.Client.UsersFetchCallCount);
        Assert.DoesNotContain(scenario.Publisher.Published, e => e is UserRemoved);
    }
}
