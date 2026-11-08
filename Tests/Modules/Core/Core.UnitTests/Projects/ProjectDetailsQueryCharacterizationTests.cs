using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

/// <summary>
/// Characterization tests for <see cref="ProjectDetailsQuery"/> on SQLite, written before its refactor to CRAP 8 or
/// below. They pin <see cref="ProjectDetailsQuery.GetProjectAsync"/>, <see cref="ProjectDetailsQuery.GetProjectsAsync"/>
/// with every optional filter and with the empty filter RemI <c>/api/remi/projects</c> drains page by page,
/// <see cref="ProjectDetailsQuery.GetProjectUsersAsync"/> and <see cref="ProjectDetailsQuery.GetProjectHeappeLinksAsync"/>.
/// </summary>
public sealed class ProjectDetailsQueryCharacterizationTests : SqliteQueryHarness
{
    private const int MemberRole = 3;
    private const int ManagerRole = 4;

    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IProjectQuery, ProjectDetailsQuery>();

    [Fact]
    public async Task GetProjectAsync_returns_the_project_with_its_live_tags_members_and_resources()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var project = await Seed.ProjectAsync("proj-a", email: "contact@example.org");
        await Seed.TagAsync(project, "tag-live");
        await Seed.TagAsync(project, "tag-gone", deleted: true);
        await Seed.MemberAsync(project, await Seed.UserAsync("u-live"), MemberRole);
        await Seed.MemberAsync(project, await Seed.UserAsync("u-removed"), MemberRole, removed: true);
        await Seed.LinkAsync(project, await Seed.ResourceAsync(acme, "res-acme", start: SqliteCoreSeed.Start.AddDays(1)));
        await Seed.LinkAsync(project, await Seed.ResourceAsync(providerId: null, "res-legacy"));
        await Seed.LinkAsync(project, await Seed.ResourceAsync(acme, "res-unlinked"), deleted: true);
        await Seed.LinkAsync(project, await Seed.ResourceAsync(acme, "res-deleted", deleted: true));

        var details = await QueryAsync(q => q.GetProjectAsync(new ProjectShortName("proj-a"), ct));

        Assert.NotNull(details);
        Assert.Equal("proj-a", details.ShortName.Value);
        Assert.Equal("proj-a", details.Name.Value);
        Assert.Null(details.Description);
        Assert.Equal("contact@example.org", details.Email?.Value);
        Assert.Null(details.ExternalId);
        Assert.Equal(EOperationMode.Unspecified, details.Mode);
        Assert.Equal(ServiceName.Default, details.ServiceName);
        Assert.Equal(SqliteCoreSeed.Start, details.StartDate);
        Assert.Equal(SqliteCoreSeed.End, details.EndDate);
        Assert.Equal("domain-proj-a", details.DomainType.Value);
        Assert.False(details.IsDeleted);
        Assert.Equal(["tag-live"], details.Tags.Select(t => t.Value));
        Assert.Equal([new ProjectUser(UserName.Create("user-u-live"), Email.Create("u-live@example.org"))], details.Users);
        Assert.Equal(
            [
                new ResourceDetail(ResourceName.Create("res-acme"), ProviderName.From("acme"), SqliteCoreSeed.Start.AddDays(1), SqliteCoreSeed.End),
                new ResourceDetail(ResourceName.Create("res-legacy"), null, SqliteCoreSeed.Start, SqliteCoreSeed.End),
            ],
            details.Resources.OrderBy(r => r.Name.Value));
    }

    [Fact]
    public async Task GetProjectAsync_returns_a_soft_deleted_project_flagged_as_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-gone", deleted: true);

        var details = await QueryAsync(q => q.GetProjectAsync(new ProjectShortName("proj-gone"), ct));

        Assert.NotNull(details);
        Assert.True(details.IsDeleted);
        Assert.Equal("domain-proj-gone", details.DomainType.Value);
        Assert.Empty(details.Tags);
        Assert.Empty(details.Users);
        Assert.Empty(details.Resources);
    }

    [Fact]
    public async Task GetProjectAsync_returns_null_for_an_unknown_short_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-a");

        var details = await QueryAsync(q => q.GetProjectAsync(new ProjectShortName("proj-missing"), ct));

        Assert.Null(details);
    }

    [Fact]
    public async Task GetProjectsAsync_with_the_remi_empty_filter_pages_every_live_project_ordered_by_short_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-c");
        await Seed.ProjectAsync("proj-a", email: "a@example.org");
        await Seed.ProjectAsync("proj-b");
        await Seed.ProjectAsync("proj-d", deleted: true);
        var remiFilter = new ProjectFilter(null, null, null, null, null);

        var first = await QueryAsync(q => q.GetProjectsAsync(remiFilter, 1, 2, ct));
        var second = await QueryAsync(q => q.GetProjectsAsync(remiFilter, 2, 2, ct));

        Assert.Equal(3, first.TotalItems);
        Assert.Equal(3, second.TotalItems);
        Assert.Equal(["proj-a", "proj-b"], first.Items.Select(p => p.ShortName.Value));
        Assert.Equal(["proj-c"], second.Items.Select(p => p.ShortName.Value));
        Assert.Equal(
            new ProjectSummary(
                new ProjectShortName("proj-a"),
                ProjectName.Create("proj-a"),
                Email.Create("a@example.org"),
                null,
                EOperationMode.Unspecified,
                EProjectState.Active,
                ServiceName.Default,
                SqliteCoreSeed.Start,
                SqliteCoreSeed.End),
            first.Items[0]);
    }

    [Fact]
    public async Task GetProjectsAsync_returns_an_empty_page_past_the_last_project()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-a");

        var page = await QueryAsync(q => q.GetProjectsAsync(new ProjectFilter(null, null, null, null, null), 2, 50, ct));

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalItems);
    }

    [Fact]
    public async Task GetProjectsAsync_narrows_to_the_caller_scope_and_an_empty_scope_matches_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-a");
        await Seed.ProjectAsync("proj-b");
        await Seed.ProjectAsync("proj-c");

        var scoped = await QueryAsync(q => q.GetProjectsAsync(
            new ProjectFilter(null, null, null, null, null, [new ProjectShortName("proj-a"), new ProjectShortName("proj-c")]), 1, 50, ct));
        var empty = await QueryAsync(q => q.GetProjectsAsync(new ProjectFilter(null, null, null, null, null, []), 1, 50, ct));

        Assert.Equal(["proj-a", "proj-c"], scoped.Items.Select(p => p.ShortName.Value));
        Assert.Equal(2, scoped.TotalItems);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalItems);
    }

    [Fact]
    public async Task GetProjectsAsync_filters_by_short_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-a");
        await Seed.ProjectAsync("proj-b");

        var page = await QueryAsync(q => q.GetProjectsAsync(
            new ProjectFilter(new ProjectShortName("proj-b"), null, null, null, null), 1, 50, ct));

        Assert.Equal(["proj-b"], page.Items.Select(p => p.ShortName.Value));
    }

    [Fact]
    public async Task GetProjectsAsync_filters_by_contact_email()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-a", email: "a@example.org");
        await Seed.ProjectAsync("proj-b", email: "b@example.org");
        await Seed.ProjectAsync("proj-c");

        var page = await QueryAsync(q => q.GetProjectsAsync(
            new ProjectFilter(null, Email.Create("b@example.org"), null, null, null), 1, 50, ct));

        Assert.Equal(["proj-b"], page.Items.Select(p => p.ShortName.Value));
    }

    [Fact]
    public async Task GetProjectsAsync_bounds_the_start_date_from_below_and_the_end_date_from_above_inclusively()
    {
        var ct = TestContext.Current.CancellationToken;
        var bound = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await Seed.ProjectAsync("starts-before", start: bound.AddDays(-1), end: bound.AddDays(30));
        await Seed.ProjectAsync("starts-on", start: bound, end: bound.AddDays(30));
        await Seed.ProjectAsync("ends-on", start: bound.AddDays(-60), end: bound);
        await Seed.ProjectAsync("ends-after", start: bound.AddDays(-60), end: bound.AddDays(1));

        var fromBound = await QueryAsync(q => q.GetProjectsAsync(new ProjectFilter(null, null, null, bound, null), 1, 50, ct));
        var untilBound = await QueryAsync(q => q.GetProjectsAsync(new ProjectFilter(null, null, null, null, bound), 1, 50, ct));

        Assert.Equal(["starts-on"], fromBound.Items.Select(p => p.ShortName.Value));
        Assert.Equal(["ends-on"], untilBound.Items.Select(p => p.ShortName.Value));
    }

    [Fact]
    public async Task GetProjectsAsync_applies_every_filter_together()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var match = await Seed.ProjectAsync("proj-a", email: "a@example.org");
        var otherEmail = await Seed.ProjectAsync("proj-b", email: "b@example.org");
        await Seed.LinkAsync(match, await Seed.ResourceAsync(acme, "res-a"));
        await Seed.LinkAsync(otherEmail, await Seed.ResourceAsync(acme, "res-b"));
        var filter = new ProjectFilter(
            new ProjectShortName("proj-a"),
            Email.Create("a@example.org"),
            DomainTypeName.Create("domain-proj-a"),
            SqliteCoreSeed.Start,
            SqliteCoreSeed.End,
            [new ProjectShortName("proj-a"), new ProjectShortName("proj-b")],
            ProviderName.From("acme"));

        var page = await QueryAsync(q => q.GetProjectsAsync(filter, 1, 50, ct));

        Assert.Equal(["proj-a"], page.Items.Select(p => p.ShortName.Value));
        Assert.Equal(1, page.TotalItems);
    }

    [Fact]
    public async Task GetProjectsAsync_returns_an_empty_page_for_an_unknown_provider()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-a");

        var page = await QueryAsync(q => q.GetProjectsAsync(
            new ProjectFilter(null, null, null, null, null, ProviderName: ProviderName.From("nobody")), 1, 50, ct));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task GetProjectUsersAsync_pages_the_live_members_with_their_project_role()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        await Seed.MemberAsync(project, await Seed.UserAsync("u-1"), MemberRole);
        await Seed.MemberAsync(project, await Seed.UserAsync("u-2"), ManagerRole);
        await Seed.MemberAsync(project, await Seed.UserAsync("u-3"), MemberRole);
        await Seed.MemberAsync(project, await Seed.UserAsync("u-removed"), MemberRole, removed: true);
        await Seed.MemberAsync(await Seed.ProjectAsync("proj-other"), await Seed.UserAsync("u-other"), MemberRole);

        var first = await QueryAsync(q => q.GetProjectUsersAsync(new ProjectShortName("proj-a"), 1, 2, ct));
        var second = await QueryAsync(q => q.GetProjectUsersAsync(new ProjectShortName("proj-a"), 2, 2, ct));

        Assert.Equal(3, first.TotalItems);
        Assert.Equal(3, second.TotalItems);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(
            [
                new ProjectUserSummary(new IdpId("u-1"), Email.Create("u-1@example.org"), UserName.Create("user-u-1"), new ProjectRoleId(MemberRole)),
                new ProjectUserSummary(new IdpId("u-2"), Email.Create("u-2@example.org"), UserName.Create("user-u-2"), new ProjectRoleId(ManagerRole)),
                new ProjectUserSummary(new IdpId("u-3"), Email.Create("u-3@example.org"), UserName.Create("user-u-3"), new ProjectRoleId(MemberRole)),
            ],
            first.Items.Concat(second.Items).OrderBy(u => u.IdentityProviderId.Value));
    }

    [Fact]
    public async Task GetProjectUsersAsync_keeps_the_total_on_a_page_past_the_last_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        await Seed.MemberAsync(project, await Seed.UserAsync("u-1"), MemberRole);

        var page = await QueryAsync(q => q.GetProjectUsersAsync(new ProjectShortName("proj-a"), 3, 10, ct));

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalItems);
    }

    [Fact]
    public async Task GetProjectUsersAsync_lists_a_deactivated_member()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        await Seed.MemberAsync(project, await Seed.UserAsync("u-off", deleted: true), MemberRole);

        var page = await QueryAsync(q => q.GetProjectUsersAsync(new ProjectShortName("proj-a"), 1, 10, ct));

        Assert.Equal("u-off", Assert.Single(page.Items).IdentityProviderId.Value);
        Assert.Equal(1, page.TotalItems);
    }

    [Theory]
    [InlineData("proj-missing")]
    [InlineData("proj-empty")]
    [InlineData("proj-removed")]
    [InlineData("proj-gone")]
    public async Task GetProjectUsersAsync_returns_an_empty_page_without_a_live_membership_of_a_live_project(string shortName)
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-empty");
        await Seed.MemberAsync(await Seed.ProjectAsync("proj-removed"), await Seed.UserAsync("u-removed"), MemberRole, removed: true);
        var gone = await Seed.ProjectAsync("proj-gone");
        await Seed.MemberAsync(gone, await Seed.UserAsync("u-gone"), MemberRole);
        await DeleteProjectAsync(gone);

        var page = await QueryAsync(q => q.GetProjectUsersAsync(new ProjectShortName(shortName), 1, 10, ct));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task GetProjectHeappeLinksAsync_returns_one_link_per_hpc_or_quantum_assignment_carrying_a_heappe_url()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var qpu = await Seed.LocationAsync(acme, "qpu", ELocationType.Quantum);
        var project = await Seed.ProjectAsync("proj-a");
        var later = await Seed.ResourceAsync(acme, "res-later", start: SqliteCoreSeed.Start.AddDays(10), end: SqliteCoreSeed.End.AddDays(10));
        var earlier = await Seed.ResourceAsync(acme, "res-earlier", start: SqliteCoreSeed.Start.AddDays(-10), end: SqliteCoreSeed.End.AddDays(-10));
        await Seed.AssignAsync(later, hpc, false, null, ("heappe_url", "https://hpc.example.org"), ("HEAPPE_IDENTIFIER", "hpc-heappe"), ("OTHER", "x"));
        await Seed.AssignAsync(earlier, qpu, false, null, ("HEAPPE_URL", "https://qpu.example.org"), ("HEAPPE_IDENTIFIER", "qpu-heappe"));
        await Seed.LinkAsync(project, later);
        await Seed.LinkAsync(project, earlier);

        var links = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-a"), ct));

        Assert.NotNull(links);
        Assert.Equal(
            [
                new ProjectHeappeLinkEntry(
                    project.Value, new Uri("https://qpu.example.org"), new UserOrg.Shared.HeappeIdentifier("qpu-heappe"), earlier.Value, "res-earlier", "qpu", qpu.ToString(),
                    SqliteCoreSeed.Start.AddDays(-10), SqliteCoreSeed.End.AddDays(-10)),
                new ProjectHeappeLinkEntry(
                    project.Value, new Uri("https://hpc.example.org"), new UserOrg.Shared.HeappeIdentifier("hpc-heappe"), later.Value, "res-later", "hpc", hpc.ToString(),
                    SqliteCoreSeed.Start, SqliteCoreSeed.End),
            ],
            links.OrderBy(l => l.ResourceName));
    }

    [Fact]
    public async Task GetProjectHeappeLinksAsync_drops_every_assignment_that_is_not_a_live_hpc_or_quantum_one_with_a_heappe_url()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var cloud = await Seed.LocationAsync(acme, "cloud", ELocationType.CLOUD);
        var retired = await Seed.LocationAsync(acme, "retired", ELocationType.HPC, deleted: true);
        var project = await Seed.ProjectAsync("proj-a");
        (string, string)[] url = [("HEAPPE_URL", "https://hpc.example.org")];

        await LinkedAssignmentAsync(acme, project, "res-cloud", cloud, url);
        await LinkedAssignmentAsync(acme, project, "res-no-values", hpc, []);
        await LinkedAssignmentAsync(acme, project, "res-no-url", hpc, [("OTHER", "x")]);
        await LinkedAssignmentAsync(acme, project, "res-retired-location", retired, url);
        var deletedAssignment = await Seed.ResourceAsync(acme, "res-deleted-assignment");
        await Seed.AssignAsync(deletedAssignment, hpc, deleted: true, values: url);
        await Seed.LinkAsync(project, deletedAssignment);
        var unlinked = await Seed.ResourceAsync(acme, "res-unlinked");
        await Seed.AssignAsync(unlinked, hpc, values: url);
        await Seed.LinkAsync(project, unlinked, deleted: true);

        var links = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-a"), ct));

        Assert.NotNull(links);
        Assert.Empty(links);
    }

    // Every HEAppE call now goes through the Nexus proxy by identifier, so a link that only carries the legacy
    // HEAPPE_URL cannot be routed and is skipped (decision Q4); there is no URL fallback.
    [Fact]
    public async Task GetProjectHeappeLinksAsync_skips_a_link_that_carries_a_heappe_url_but_no_heappe_identifier()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var project = await Seed.ProjectAsync("proj-a");
        await LinkedAssignmentAsync(acme, project, "res-url-only", hpc, [("HEAPPE_URL", "https://hpc.example.org")]);

        var links = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-a"), ct));

        Assert.NotNull(links);
        Assert.Empty(links);
    }

    [Fact]
    public async Task GetProjectHeappeLinksAsync_collapses_links_to_the_same_resource_and_location()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "hpc", ELocationType.HPC);
        var project = await Seed.ProjectAsync("proj-a");
        var resource = await Seed.ResourceAsync(acme, "res-a");
        await Seed.AssignAsync(resource, hpc, false, null, ("HEAPPE_URL", "https://one.example.org"), ("HEAPPE_IDENTIFIER", "one"));
        await Seed.AssignAsync(resource, hpc, false, null, ("HEAPPE_URL", "https://two.example.org"), ("HEAPPE_IDENTIFIER", "two"));
        await Seed.LinkAsync(project, resource);

        var links = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-a"), ct));

        Assert.NotNull(links);
        var link = Assert.Single(links);
        Assert.Equal(("res-a", "hpc"), (link.ResourceName, link.LocationName));
        Assert.Contains(link.HeappeApi.ToString(), (string[])["https://one.example.org/", "https://two.example.org/"]);
    }

    [Fact]
    public async Task GetProjectHeappeLinksAsync_returns_an_empty_list_for_a_project_without_resources_or_matching_locations()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        await Seed.ProjectAsync("proj-bare");
        var cloudOnly = await Seed.ProjectAsync("proj-cloud");
        await LinkedAssignmentAsync(acme, cloudOnly, "res-cloud", await Seed.LocationAsync(acme, "cloud", ELocationType.CLOUD), [("HEAPPE_URL", "https://c.example.org")]);

        var bare = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-bare"), ct));
        var cloud = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-cloud"), ct));

        Assert.NotNull(bare);
        Assert.Empty(bare);
        Assert.NotNull(cloud);
        Assert.Empty(cloud);
    }

    [Theory]
    [InlineData("proj-missing")]
    [InlineData("proj-gone")]
    public async Task GetProjectHeappeLinksAsync_returns_null_for_an_unknown_or_soft_deleted_project(string shortName)
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.ProjectAsync("proj-gone", deleted: true);

        var links = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName(shortName), ct));

        Assert.Null(links);
    }

    private async Task LinkedAssignmentAsync(
        ProviderId provider, ProjectId project, string resourceName, Guid location, (string Name, string Value)[] values)
    {
        var resource = await Seed.ResourceAsync(provider, resourceName);
        await Seed.AssignAsync(resource, location, values: values);
        await Seed.LinkAsync(project, resource);
    }

    private async Task DeleteProjectAsync(ProjectId projectId)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var project = await context.Set<ProjectAggregate>()
            .SingleAsync(p => EF.Property<ProjectId>(p, "Id") == projectId, TestContext.Current.CancellationToken);
        Assert.True(project.Delete().IsSuccess);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> QueryAsync<T>(Func<IProjectQuery, Task<T>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IProjectQuery>());
    }
}
