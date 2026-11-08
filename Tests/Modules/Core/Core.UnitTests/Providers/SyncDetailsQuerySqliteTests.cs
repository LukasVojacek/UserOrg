using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.DependencyInjection;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for <see cref="SyncDetailsQuery.GetSyncDetailsAsync"/> on SQLite, the read behind DDI
/// <c>api/v2/Service</c>, written before its refactor to CRAP 8 or below. <see cref="SyncDetailsQueryTests"/> runs the
/// same aggregation on EF InMemory, which evaluates untranslatable expressions in memory; these run it as SQL.
/// </summary>
public sealed class SyncDetailsQuerySqliteTests : SqliteQueryHarness
{
    private const int OwnRole = 5;
    private const int ForeignRole = 6;
    private const int OtherRole = 7;

    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<ISyncDetailsQuery, SyncDetailsQuery>();

    private protected override async Task SeedAsync(DatabaseContext context)
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var locationType in (ELocationType[])[ELocationType.HPC, ELocationType.CLOUD])
        {
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO LocationType (Id, Name, Description) VALUES ({(int)locationType}, {locationType.ToString()}, NULL)", ct);
        }
    }

    [Fact]
    public async Task Lists_the_providers_own_users_first_with_roles_on_its_projects_then_every_other_user_with_all_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var other = await Seed.ProviderAsync("other");
        var own = await ProjectAtAsync(acme, "own-proj", "own-res", await Seed.LocationAsync(acme, "own-hpc"));
        var foreign = await ProjectAtAsync(other, "foreign-proj", "foreign-res", await Seed.LocationAsync(other, "foreign-cloud", ELocationType.CLOUD));

        var outsider = await Seed.UserAsync("outsider");
        var insider = await Seed.UserAsync("insider");
        await Seed.UserAsync("loner");
        await Seed.MemberAsync(own, insider, OwnRole);
        await Seed.MemberAsync(foreign, insider, ForeignRole);
        await Seed.MemberAsync(foreign, outsider, OtherRole);
        await Seed.MemberAsync(own, outsider, OtherRole, removed: true);

        var users = await QueryAsync(acme, ct);

        Assert.Equal("insider", users[0].KeycloakSid);
        Assert.Equal(["insider", "loner", "outsider"], users.Select(u => u.KeycloakSid).Order().TakeLast(3));
        Assert.Equal([("own-proj", OwnRole)], Roles(users[0]));
        Assert.Equal([("foreign-proj", OtherRole)], Roles(users.Single(u => u.KeycloakSid == "outsider")));
        Assert.Empty(users.Single(u => u.KeycloakSid == "loner").Roles);
    }

    [Fact]
    public async Task Projects_the_user_and_each_roles_resources_locations_and_values()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var hpc = await Seed.LocationAsync(acme, "own-hpc");
        var project = await Seed.ProjectAsync("own-proj");
        var resource = await Seed.ResourceAsync(acme, "own-res");
        await Seed.AssignAsync(resource, hpc, values: [("NODES", "4")]);
        await Seed.LinkAsync(project, resource);
        await Seed.LinkAsync(project, await Seed.ResourceAsync(acme, "unlinked-res"), deleted: true);
        await Seed.LinkAsync(project, await Seed.ResourceAsync(acme, "deleted-res", deleted: true));
        var user = await Seed.UserAsync("insider");
        await Seed.MemberAsync(project, user, OwnRole);

        var views = await QueryAsync(acme, ct);
        Assert.NotNull(views);
        var view = views.FirstOrDefault();

        Assert.Equal(user.Value, view.Id);
        Assert.Equal(("insider", "user-insider", "insider@example.org", false), (view.KeycloakSid, view.UserName, view.Email, view.IsDeleted));
        var role = Assert.Single(view.Roles);
        Assert.Equal(("own-proj", "insider@example.org", OwnRole), (role.ProjectShortName, role.UserEmail, role.SystemRoleId));

        var projectResource = Assert.Single(role.ProjectResources.Where(w => w.State != EProjectResourceState.Deleted));
        Assert.Equal(("own-res", "own-res description", SqliteCoreSeed.Start, SqliteCoreSeed.End),
            (projectResource.Name, projectResource.Description, projectResource.StartDate!.Value, projectResource.EndDate!.Value));
        var location = Assert.Single(projectResource.Locations);
        Assert.Equal(
            ("own-hpc", "own-hpc description", "https://own-hpc.example.org", false, "HPC", (long?)10, EAllocationUnitType.Unspecified),
            (location.Name, location.Description, location.Endpoint, location.IsPrivate, location.Type, location.AllocationUnitCount, location.AllocationUnitType));
        Assert.Equal([new SyncLocationValueView("NODES", "4")], location.Values);
    }

    [Fact]
    public async Task Reports_a_deactivated_user_with_is_deleted_set()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        await Seed.UserAsync("gone", deleted: true);

        var view = Assert.Single(await QueryAsync(acme, ct));

        Assert.Equal("gone", view.KeycloakSid);
        Assert.True(view.IsDeleted);
    }

    [Fact]
    public async Task A_soft_deleted_project_is_not_one_of_the_providers_projects()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var gone = await ProjectAtAsync(acme, "gone-proj", "gone-res", await Seed.LocationAsync(acme, "own-hpc"));
        var live = await ProjectAtAsync(acme, "live-proj", "live-res", await Seed.LocationAsync(acme, "own-hpc-2"));
        var user = await Seed.UserAsync("member");
        var other = await Seed.UserAsync("other");
        await Seed.MemberAsync(live, other, OtherRole);
        await Seed.MemberAsync(gone, user, OwnRole);
        await DeleteProjectAsync(gone);

        var users = await QueryAsync(acme, ct);

        Assert.Equal("other", users[0].KeycloakSid);
        Assert.Equal(["member", "other"], users.Select(u => u.KeycloakSid).Order().TakeLast(2));
    }

    [Fact]
    public async Task An_unknown_provider_lists_every_user_with_all_their_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var project = await ProjectAtAsync(acme, "own-proj", "own-res", await Seed.LocationAsync(acme, "own-hpc"));
        await Seed.MemberAsync(project, await Seed.UserAsync("member"), OwnRole);

        var users = await ServiceProviderQueryAsync(Guid.NewGuid(), ct);

        Assert.NotNull(users);
        var view = users.LastOrDefault();
        Assert.Equal([("own-proj", OwnRole)], Roles(view));
    }

    private static (string, int)[] Roles(SyncUserView view) =>
        [.. view.Roles.Select(r => (r.ProjectShortName, r.SystemRoleId)).Order()];

    private async Task<ProjectId> ProjectAtAsync(ProviderId provider, string shortName, string resourceName, Guid location)
    {
        var project = await Seed.ProjectAsync(shortName);
        var resource = await Seed.ResourceAsync(provider, resourceName);
        await Seed.AssignAsync(resource, location);
        await Seed.LinkAsync(project, resource);
        return project;
    }

    private async Task DeleteProjectAsync(ProjectId projectId)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var project = await context.Set<Core.Domain.Project.Project>()
            .SingleAsync(p => EF.Property<ProjectId>(p, "Id") == projectId, TestContext.Current.CancellationToken);
        Assert.True(project.Delete().IsSuccess);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private Task<IReadOnlyList<SyncUserView>> QueryAsync(ProviderId provider, CancellationToken ct) =>
        ServiceProviderQueryAsync(provider.Value, ct);

    private async Task<IReadOnlyList<SyncUserView>> ServiceProviderQueryAsync(Guid providerId, CancellationToken ct)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISyncDetailsQuery>().GetSyncDetailsAsync(providerId, ct);
    }
}
