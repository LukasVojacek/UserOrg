using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.UserInfo;

/// <summary>
/// Characterization tests for <see cref="UserDetailsQuery.GetUsersAsync"/> on SQLite, written before its refactor to
/// CRAP 8 or below. RemI <c>/api/remi/users</c> drains it page by page with an empty <see cref="UserFilter"/>; the
/// v2 users endpoint passes the project and identity-provider filters.
/// </summary>
public sealed class UserDetailsQueryGetUsersCharacterizationTests : SqliteQueryHarness
{
    private const int MemberRole = 3;

    private static readonly UserFilter RemiFilter = new(null, null);

    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IUserInfoQuery, UserDetailsQuery>();

    [Fact]
    public async Task The_remi_empty_filter_pages_every_active_user_ordered_by_identity_provider_id()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.UserAsync("c-user");
        await Seed.UserAsync("a-user");
        await Seed.UserAsync("b-user");
        await Seed.UserAsync("d-off", deleted: true);

        var first = await QueryAsync(q => q.GetUsersAsync(RemiFilter, 1, 2, ct));
        var second = await QueryAsync(q => q.GetUsersAsync(RemiFilter, 2, 2, ct));

        Assert.Equal(3, first.TotalItems);
        Assert.Equal(3, second.TotalItems);
        Assert.Equal(["a-user", "b-user"], first.Items.Select(u => u.IdentityProviderId.Value));
        Assert.Equal(["c-user"], second.Items.Select(u => u.IdentityProviderId.Value));
    }

    [Fact]
    public async Task Each_user_carries_its_live_project_memberships_and_its_assigned_provider_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var retired = await Seed.ProviderAsync("retired", deactivated: true);
        var member = await Seed.UserAsync("a-user", assignedProvider: acme);
        await Seed.UserAsync("b-user");
        await Seed.UserAsync("c-user", assignedProvider: retired);
        await Seed.MemberAsync(await Seed.ProjectAsync("proj-x"), member, MemberRole);
        await Seed.MemberAsync(await Seed.ProjectAsync("proj-y"), member, MemberRole);
        await Seed.MemberAsync(await Seed.ProjectAsync("proj-z"), member, MemberRole, removed: true);

        var page = await QueryAsync(q => q.GetUsersAsync(RemiFilter, 1, 50, ct));

        Assert.Equal(3, page.TotalItems);
        var a = page.Items[0];
        Assert.Equal(("a-user", "a-user@example.org", "user-a-user"), (a.IdentityProviderId.Value, a.Email.Value, a.UserName.Value));
        Assert.Equal(["proj-x", "proj-y"], a.Projects.Select(p => p.Value).Order());
        Assert.Equal("acme", a.AssignedProviderName?.Value);
        Assert.Empty(page.Items[1].Projects);
        Assert.Null(page.Items[1].AssignedProviderName);
        Assert.Equal("retired", page.Items[2].AssignedProviderName?.Value);
    }

    [Fact]
    public async Task The_identity_provider_filter_returns_that_user_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.UserAsync("a-user");
        await Seed.UserAsync("b-user");

        var page = await QueryAsync(q => q.GetUsersAsync(new UserFilter(null, new IdpId("b-user")), 1, 50, ct));

        Assert.Equal(["b-user"], page.Items.Select(u => u.IdentityProviderId.Value));
        Assert.Equal(1, page.TotalItems);
    }

    [Fact]
    public async Task The_project_filter_returns_the_live_members_of_that_project()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-x");
        await Seed.MemberAsync(project, await Seed.UserAsync("a-user"), MemberRole);
        await Seed.MemberAsync(project, await Seed.UserAsync("b-user"), MemberRole, removed: true);
        await Seed.MemberAsync(project, await Seed.UserAsync("c-user"), MemberRole);
        await Seed.MemberAsync(await Seed.ProjectAsync("proj-y"), await Seed.UserAsync("d-user"), MemberRole);

        var page = await QueryAsync(q => q.GetUsersAsync(new UserFilter(new ProjectShortName("proj-x"), null), 1, 50, ct));

        Assert.Equal(["a-user", "c-user"], page.Items.Select(u => u.IdentityProviderId.Value));
        Assert.Equal(2, page.TotalItems);
    }

    [Fact]
    public async Task Both_filters_together_return_only_a_user_matching_each()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-x");
        await Seed.MemberAsync(project, await Seed.UserAsync("a-user"), MemberRole);
        await Seed.MemberAsync(project, await Seed.UserAsync("b-user"), MemberRole);
        await Seed.UserAsync("c-user");

        var match = await QueryAsync(q => q.GetUsersAsync(new UserFilter(new ProjectShortName("proj-x"), new IdpId("b-user")), 1, 50, ct));
        var outside = await QueryAsync(q => q.GetUsersAsync(new UserFilter(new ProjectShortName("proj-x"), new IdpId("c-user")), 1, 50, ct));

        Assert.Equal(["b-user"], match.Items.Select(u => u.IdentityProviderId.Value));
        Assert.Empty(outside.Items);
        Assert.Equal(0, outside.TotalItems);
    }

    [Theory]
    [InlineData("proj-missing")]
    [InlineData("proj-removed")]
    public async Task The_project_filter_returns_an_empty_page_without_a_live_membership(string shortName)
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.MemberAsync(await Seed.ProjectAsync("proj-removed"), await Seed.UserAsync("a-user"), MemberRole, removed: true);

        var page = await QueryAsync(q => q.GetUsersAsync(new UserFilter(new ProjectShortName(shortName), null), 1, 50, ct));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task A_page_past_the_last_user_is_empty_and_keeps_the_total()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.UserAsync("a-user");

        var page = await QueryAsync(q => q.GetUsersAsync(RemiFilter, 3, 50, ct));

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalItems);
    }

    [Fact]
    public async Task An_unknown_identity_provider_id_returns_an_empty_page()
    {
        var ct = TestContext.Current.CancellationToken;
        await Seed.UserAsync("a-user");

        var page = await QueryAsync(q => q.GetUsersAsync(new UserFilter(null, new IdpId("nobody")), 1, 50, ct));

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    private async Task<QueryPage<UserSummary>> QueryAsync(Func<IUserInfoQuery, Task<QueryPage<UserSummary>>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IUserInfoQuery>());
    }
}
