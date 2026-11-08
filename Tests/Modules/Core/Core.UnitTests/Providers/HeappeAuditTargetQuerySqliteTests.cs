using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for <see cref="HeappeAuditTargetQuery.GetAuditTargetsAsync"/> on SQLite, called the way
/// its two consumers call it: Heappe <c>/api/Heappe/ResourceAudit/run</c> and <c>HeappeResourceAuditWorker</c>
/// reach it through <c>HeappeAuditTargetProvider</c>, which passes nothing but the cancellation token. The tests
/// pin which (resource, project) pairs become targets and every reason a candidate is dropped.
/// <para>
/// Every test that gets past the first read is red today: the assigned-location read filters on
/// <c>EF.Property&lt;ProjectResourceId&gt;(al, "ProjectResourceId").Value</c>, which no relational provider can
/// translate, so the query throws <see cref="InvalidOperationException"/> whenever a valid linked resource exists.
/// EF InMemory evaluates it in memory, which is why <c>HeappeAuditTargetQueryTests</c> stays green.
/// </para>
/// </summary>
public sealed class HeappeAuditTargetQuerySqliteTests : SqliteQueryHarness
{
    private static readonly DateTimeOffset Now = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddScoped<IHeappeAuditTargetQuery, HeappeAuditTargetQuery>();
    }

    [Fact]
    public async Task Lists_one_target_per_live_project_link_of_every_valid_resource_at_an_hpc_or_quantum_location()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var quantum = await Seed.LocationAsync(acme, "qpu", ELocationType.Quantum);
        var projectA = await Seed.ProjectAsync("proj-a");
        var projectB = await Seed.ProjectAsync("proj-b");
        var projectC = await Seed.ProjectAsync("proj-c");

        var shared = await Seed.ResourceAsync(acme, "shared-res");
        await Seed.AssignAsync(shared, hpc, values: [(ApplicationConstants.HeappeUriKey, "https://hpc.example.org"), (ApplicationConstants.HeappeIdentifierKey, "hpc-1"), ("OTHER", "ignored")]);
        await Seed.LinkAsync(projectA, shared);
        await Seed.LinkAsync(projectB, shared);
        await Seed.LinkAsync(projectC, shared, deleted: true);

        var qpu = await Seed.ResourceAsync(acme, "qpu-res");
        await Seed.AssignAsync(qpu, quantum, values: [(ApplicationConstants.HeappeUriKey, "https://qpu.example.org"), (ApplicationConstants.HeappeIdentifierKey, "qpu-1")]);
        await Seed.LinkAsync(projectC, qpu);

        var targets = await QueryAsync(q => q.GetAuditTargetsAsync(ct));

        Assert.Equal(
            [
                new HeappeAuditTargetRow("qpu-res", "proj-c", "https://qpu.example.org", "qpu-1", SqliteCoreSeed.Start, SqliteCoreSeed.End),
                new HeappeAuditTargetRow("shared-res", "proj-a", "https://hpc.example.org", "hpc-1", SqliteCoreSeed.Start, SqliteCoreSeed.End),
                new HeappeAuditTargetRow("shared-res", "proj-b", "https://hpc.example.org", "hpc-1", SqliteCoreSeed.Start, SqliteCoreSeed.End),
            ],
            targets.OrderBy(t => t.ResourceName).ThenBy(t => t.ProjectShortName));
    }

    [Fact]
    public async Task Drops_every_candidate_that_is_not_currently_valid_linked_assigned_and_identified()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var cloud = await Seed.LocationAsync(acme, "cloud", ELocationType.CLOUD);
        var retiredHpc = await Seed.LocationAsync(acme, "retired-hpc", ELocationType.HPC, deleted: true);
        var project = await Seed.ProjectAsync("proj-a");
        (string, string)[] complete = [(ApplicationConstants.HeappeUriKey, "https://hpc.example.org"), (ApplicationConstants.HeappeIdentifierKey, "inst")];

        await CandidateAsync(acme, project, "expired", hpc, complete, end: Now.AddDays(-1));
        await CandidateAsync(acme, project, "future", hpc, complete, start: Now.AddDays(1));
        await CandidateAsync(acme, project, "deleted-res", hpc, complete, deletedResource: true);
        await CandidateAsync(acme, project, "unlinked", hpc, complete, deletedLink: true);
        await CandidateAsync(acme, project, "cloud", cloud, complete);
        await CandidateAsync(acme, project, "retired-location", retiredHpc, complete);
        await CandidateAsync(acme, project, "deleted-assignment", hpc, complete, deletedAssignment: true);
        await CandidateAsync(acme, project, "no-values", hpc, []);
        await CandidateAsync(acme, project, "no-identifier", hpc, [(ApplicationConstants.HeappeUriKey, "https://hpc.example.org")]);
        await CandidateAsync(acme, project, "no-url", hpc, [(ApplicationConstants.HeappeIdentifierKey, "inst")]);
        await CandidateAsync(acme, project, "blank-url", hpc, [(ApplicationConstants.HeappeUriKey, " "), (ApplicationConstants.HeappeIdentifierKey, "inst")]);
        await CandidateAsync(acme, project, "blank-identifier", hpc, [(ApplicationConstants.HeappeUriKey, "https://hpc.example.org"), (ApplicationConstants.HeappeIdentifierKey, "")]);
        await CandidateAsync(acme, project, "control", hpc, complete);

        var targets = await QueryAsync(q => q.GetAuditTargetsAsync(ct));

        Assert.Equal(["blank-url", "control", "no-url"], targets.Select(t => t.ResourceName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Lists_a_target_without_a_url_by_its_identifier_alone()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var project = await Seed.ProjectAsync("proj-a");
        await CandidateAsync(acme, project, "no-url", hpc, [(ApplicationConstants.HeappeIdentifierKey, "inst")]);

        var targets = await QueryAsync(q => q.GetAuditTargetsAsync(ct));

        Assert.Equal(
            [new HeappeAuditTargetRow("no-url", "proj-a", null, "inst", SqliteCoreSeed.Start, SqliteCoreSeed.End)],
            targets);
    }

    [Fact]
    public async Task Returns_nothing_when_no_resource_is_currently_valid_and_linked()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var project = await Seed.ProjectAsync("proj-a");
        var expired = await Seed.ResourceAsync(acme, "expired", end: Now.AddDays(-1));
        await Seed.AssignAsync(expired, hpc, values: [(ApplicationConstants.HeappeUriKey, "https://hpc.example.org"), (ApplicationConstants.HeappeIdentifierKey, "inst")]);
        await Seed.LinkAsync(project, expired);

        var targets = await QueryAsync(q => q.GetAuditTargetsAsync(ct));

        Assert.Empty(targets);
    }

    [Fact]
    public async Task Returns_nothing_when_no_valid_resource_is_assigned_to_an_hpc_or_quantum_location()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var cloud = await Seed.LocationAsync(acme, "cloud", ELocationType.CLOUD);
        var project = await Seed.ProjectAsync("proj-a");
        var resource = await Seed.ResourceAsync(acme, "cloud-res");
        await Seed.AssignAsync(resource, cloud, values: [(ApplicationConstants.HeappeUriKey, "https://cloud.example.org"), (ApplicationConstants.HeappeIdentifierKey, "inst")]);
        await Seed.LinkAsync(project, resource);

        var targets = await QueryAsync(q => q.GetAuditTargetsAsync(ct));

        Assert.Empty(targets);
    }

    private async Task CandidateAsync(
        ProviderId provider,
        ProjectId project,
        string name,
        Guid location,
        (string Name, string Value)[] values,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        bool deletedResource = false,
        bool deletedLink = false,
        bool deletedAssignment = false)
    {
        var resource = await Seed.ResourceAsync(provider, name, start, end, deleted: deletedResource);
        await Seed.AssignAsync(resource, location, deleted: deletedAssignment, values: values);
        await Seed.LinkAsync(project, resource, deleted: deletedLink);
    }

    private async Task<T> QueryAsync<T>(Func<IHeappeAuditTargetQuery, Task<T>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IHeappeAuditTargetQuery>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
