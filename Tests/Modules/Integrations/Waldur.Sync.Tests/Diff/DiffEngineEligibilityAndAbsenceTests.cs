using Waldur.Contracts.Events;
using Waldur.Sync.Diff;

using Xunit;

namespace Waldur.Sync.Tests.Diff;

public sealed class DiffEngineEligibilityAndAbsenceTests
{
    [Fact]
    public void Should_EmitNoEvents_When_ProjectIneligible_WithNoSnapshot()
    {
        // An ineligible project (null payload) carries a NULL membership snapshot — the worker never
        // fetches members for it — so it emits nothing (ADR-0054/A4).
        var input = new DiffInput
        {
            Projects = [new ProjectDiffInput(DiffTestData.ProjectId, Payload: null, Membership: null)]
        };

        var result = DiffTestData.Engine().Diff(input, new ShaBreadcrumbMap());

        Assert.Empty(result.Actions);
        Assert.Empty(result.Absences);
    }

    [Fact]
    public void Should_EmitMembership_When_ProjectCarriedForward_WithCompleteSnapshot()
    {
        // A resource-fetch-failed project is carried forward (null payload) but its members still flow
        // from a complete snapshot (D12/ADR-0063) — diverging deliberately from legacy's membership skip.
        var input = new DiffInput
        {
            Projects =
            [
                new ProjectDiffInput(DiffTestData.ProjectId, Payload: null,
                    MembershipSnapshot.Complete([DiffTestData.Member("alice")]))
            ]
        };

        var result = DiffTestData.Engine().Diff(input, new ShaBreadcrumbMap());

        Assert.Contains(result.Actions, a => a.Event is UserAssigned);
        Assert.Empty(result.Absences);
    }

    [Fact]
    public void Should_NotReportAbsence_When_IneligibleProjectStillPresent()
    {
        var project = DiffTestData.Project();
        var map = DiffTestData.MapWith((ShaKey.Project(project.ExternalId), project));
        var input = new DiffInput
        {
            Projects = [new ProjectDiffInput(project.ExternalId, Payload: null)]
        };

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Absences);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Should_ReportAbsenceTransition_WithoutEvent_When_ProjectVanished()
    {
        var project = DiffTestData.Project();
        var map = DiffTestData.MapWith((ShaKey.Project(project.ExternalId), project));
        var input = new DiffInput { Projects = [] };

        var result = DiffTestData.Engine().Diff(input, map);

        var absence = Assert.Single(result.Absences);
        Assert.Equal(ShaKey.Project(project.ExternalId), absence.Key);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Should_ReportAbsenceTransition_When_ProviderVanished()
    {
        var provider = DiffTestData.Provider();
        var map = DiffTestData.MapWith((ShaKey.Provider(provider.ExternalId), provider));
        var input = new DiffInput { Providers = [] };

        var result = DiffTestData.Engine().Diff(input, map);

        var absence = Assert.Single(result.Absences);
        Assert.Equal(ShaKey.Provider(provider.ExternalId), absence.Key);
    }

    [Fact]
    public void Should_SuppressProviderAbsenceSweep_When_ProvidersIncomplete()
    {
        // ADR-0064: a provider known to the map but absent from an INCOMPLETE provider list may merely
        // sit on the failed page — the sweep is suppressed so its breadcrumb survives.
        var provider = DiffTestData.Provider();
        var map = DiffTestData.MapWith((ShaKey.Provider(provider.ExternalId), provider));
        var input = new DiffInput { Providers = [], ProvidersComplete = false };

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Absences);
    }

    [Fact]
    public void Should_SuppressProjectAbsenceSweep_When_ProjectsIncomplete()
    {
        var project = DiffTestData.Project();
        var map = DiffTestData.MapWith((ShaKey.Project(project.ExternalId), project));
        var input = new DiffInput { Projects = [], ProjectsComplete = false };

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Absences);
    }

    [Fact]
    public void Should_SweepOnlyCompleteType_When_OneListIncomplete()
    {
        // The gate is per entity type: an incomplete provider list suppresses only the provider sweep,
        // while a complete project list still sweeps a genuinely vanished project.
        var provider = DiffTestData.Provider();
        var project = DiffTestData.Project();
        var map = DiffTestData.MapWith(
            (ShaKey.Provider(provider.ExternalId), provider),
            (ShaKey.Project(project.ExternalId), project));
        var input = new DiffInput { Providers = [], Projects = [], ProvidersComplete = false, ProjectsComplete = true };

        var result = DiffTestData.Engine().Diff(input, map);

        var absence = Assert.Single(result.Absences);
        Assert.Equal(ShaKey.Project(project.ExternalId), absence.Key);
    }

    [Fact]
    public void Should_NotReportMemberKeys_InAbsenceSweep()
    {
        // A vanished project's member SHAs are left untouched (only complete snapshots drive UserRemoved).
        var project = DiffTestData.Project();
        var map = DiffTestData.MapWith(
            (ShaKey.Project(project.ExternalId), project),
            (ShaKey.Member(project.ExternalId, "alice"), DiffTestData.Member("alice")));
        var input = new DiffInput { Projects = [] };

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.DoesNotContain(result.Absences, a => a.Key.EntityType == WaldurEntityType.Member);
        Assert.DoesNotContain(result.Actions, a => a.Event is UserRemoved);
    }
}
