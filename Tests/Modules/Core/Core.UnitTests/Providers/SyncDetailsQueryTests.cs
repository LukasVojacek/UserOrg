using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Domain;
using Core.Domain.Provider;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;
using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Tests for <see cref="SyncDetailsQuery"/> over the EF Core in-memory provider, covering the legacy provider
/// "SyncDetails" aggregation reproduced in D5: group 1 (the provider's own users, role entries filtered to the
/// provider's projects), group 2 (every other user, role entries for all their projects), the
/// <c>SystemRoleOnly</c> named-filter bypass that makes project roles visible, role <b>ids</b> in the view
/// (not names), and soft-deleted users reported with IsDeleted = true. Mirrors the harness in <see cref="DdiProjectsInfoQueryTests"/>.
/// </summary>
public sealed class SyncDetailsQueryTests : IAsyncDisposable
{
    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2030, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private const int RoleId = 42;

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 6000;

    public SyncDetailsQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<ISyncDetailsQuery, SyncDetailsQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_FilterRolesToProviderProjects_When_UserBelongsToProvider()
    {
        // A provider with one project (via its location-provider link), and a second project NOT linked to
        // the provider. The provider's user has a project role in BOTH; only the provider-project role is
        // emitted for that user (group 1 filtering).
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();

        var resourceId = ProjectResourceId.New();
        await SeedProviderWithLocationAndResourceAsync(providerId, "prov-loc", resourceId);

        var providerProject = await SeedProjectAsync("prov-proj");
        await SeedResourceToProjectAsync(resourceId, providerProject);

        var otherProject = await SeedProjectAsync("other-proj");

        var user = await SeedUserAsync("provider-user", "pu@example.com");
        await AssignUserToProjectsWithRoleAsync(user, [providerProject, otherProject], RoleId);

        var result = await QueryAsync(providerId, ct);

        var view = Assert.Single(result, u => u.KeycloakSid == "provider-user");
        var role = Assert.Single(view.Roles);
        Assert.Equal("prov-proj", role.ProjectShortName);
        Assert.Equal(RoleId, role.SystemRoleId);
        Assert.Equal("pu@example.com", role.UserEmail);
    }

    [Fact]
    public async Task Should_EmitAllProjectRoles_When_UserIsNotProviderUser()
    {
        // A user with NO membership in any provider project is a group-2 user: every project they have a
        // role for is emitted (no filtering).
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();

        var resourceId = ProjectResourceId.New();
        await SeedProviderWithLocationAndResourceAsync(providerId, "prov-loc", resourceId);
        var providerProject = await SeedProjectAsync("prov-proj");
        await SeedResourceToProjectAsync(resourceId, providerProject);

        var projectA = await SeedProjectAsync("a-proj");
        var projectB = await SeedProjectAsync("b-proj");

        var outsider = await SeedUserAsync("outsider", "out@example.com");
        await AssignUserToProjectsWithRoleAsync(outsider, [projectA, projectB], RoleId);

        var result = await QueryAsync(providerId, ct);

        var view = Assert.Single(result, u => u.KeycloakSid == "outsider");
        Assert.Equal(2, view.Roles.Count);
        Assert.Contains(view.Roles, r => r.ProjectShortName == "a-proj");
        Assert.Contains(view.Roles, r => r.ProjectShortName == "b-proj");
    }

    [Fact]
    public async Task Should_ReturnProjectRoles_When_SystemRoleOnlyFilterIsBypassed()
    {
        // Project-scoped UserPermission rows have ProjectId != null and are hidden by the default
        // "SystemRoleOnly" query filter. The query must bypass it; otherwise the user has zero roles.
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();

        var resourceId = ProjectResourceId.New();
        await SeedProviderWithLocationAndResourceAsync(providerId, "prov-loc", resourceId);
        var providerProject = await SeedProjectAsync("prov-proj");
        await SeedResourceToProjectAsync(resourceId, providerProject);

        var user = await SeedUserAsync("member", "m@example.com");
        await AssignUserToProjectsWithRoleAsync(user, [providerProject], RoleId);

        var result = await QueryAsync(providerId, ct);

        var view = Assert.Single(result, u => u.KeycloakSid == "member");
        var role = Assert.Single(view.Roles);
        Assert.Equal("prov-proj", role.ProjectShortName);
        Assert.Equal(RoleId, role.SystemRoleId);
    }

    [Fact]
    public async Task Should_ReportSoftDeletedUserWithIsDeletedTrue_When_Projecting()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();

        var resourceId = ProjectResourceId.New();
        await SeedProviderWithLocationAndResourceAsync(providerId, "prov-loc", resourceId);
        var providerProject = await SeedProjectAsync("prov-proj");
        await SeedResourceToProjectAsync(resourceId, providerProject);

        var live = await SeedUserAsync("live", "live@example.com");
        await AssignUserToProjectsWithRoleAsync(live, [providerProject], RoleId);

        await SeedDeletedUserAsync("ghost", "ghost@example.com");

        var result = await QueryAsync(providerId, ct);

        var ghost = Assert.Single(result, u => u.KeycloakSid == "ghost");
        Assert.True(ghost.IsDeleted);
        var liveView = Assert.Single(result, u => u.KeycloakSid == "live");
        Assert.False(liveView.IsDeleted);
    }

    [Fact]
    public async Task Should_EmitEachUserOnce_When_BothGroupsPresent()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();

        var resourceId = ProjectResourceId.New();
        await SeedProviderWithLocationAndResourceAsync(providerId, "prov-loc", resourceId);
        var providerProject = await SeedProjectAsync("prov-proj");
        await SeedResourceToProjectAsync(resourceId, providerProject);

        var providerUser = await SeedUserAsync("provider-user", "pu@example.com");
        await AssignUserToProjectsWithRoleAsync(providerUser, [providerProject], RoleId);

        var outProject = await SeedProjectAsync("out-proj");
        var outsider = await SeedUserAsync("outsider", "out@example.com");
        await AssignUserToProjectsWithRoleAsync(outsider, [outProject], RoleId);

        var result = await QueryAsync(providerId, ct);

        Assert.Equal(2, result.Count);
        Assert.Single(result, u => u.KeycloakSid == "provider-user");
        Assert.Single(result, u => u.KeycloakSid == "outsider");
    }

    [Fact]
    public async Task Should_CarryAllocationUnitTypeAsEnum_When_ProjectingLocations()
    {
        var ct = TestContext.Current.CancellationToken;
        var providerId = ProviderId.New();

        var resourceId = ProjectResourceId.New();
        await SeedLocationTypeAsync(ELocationType.HPC);
        await SeedProviderWithLocationAndResourceAsync(providerId, "prov-loc", resourceId, (int)EAllocationUnitType.NodeHours);
        var providerProject = await SeedProjectAsync("prov-proj");
        await SeedResourceToProjectAsync(resourceId, providerProject);

        var user = await SeedUserAsync("member", "m@example.com");
        await AssignUserToProjectsWithRoleAsync(user, [providerProject], RoleId);

        var result = await QueryAsync(providerId, ct);

        var role = Assert.Single(Assert.Single(result, u => u.KeycloakSid == "member").Roles);
        var location = Assert.Single(Assert.Single(role.ProjectResources).Locations);
        Assert.Equal("HPC", location.Type);
        Assert.Equal(EAllocationUnitType.NodeHours, location.AllocationUnitType);
    }

    private async Task<IReadOnlyList<SyncUserView>> QueryAsync(ProviderId providerId, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<ISyncDetailsQuery>();
        return await query.GetSyncDetailsAsync(providerId.Value, ct);
    }

    private static async Task SaveAsync(DatabaseContext context, CancellationToken ct)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            if (entry.Metadata.FindProperty("ServiceName") is not null
                && entry.Property("ServiceName").CurrentValue is null)
            {
                entry.Property("ServiceName").CurrentValue = "LEXIS";
            }

            // The in-memory provider does not run NEWID() SQL defaults, so DB-generated Guid "Id" keys
            // (e.g. UserPermission, AssignedLocation) stay Guid.Empty and collide across rows. Assign a
            // fresh id to any added entity whose Guid "Id" key is still empty.
            if (entry.Metadata.FindProperty("Id") is { ClrType: var t } && t == typeof(Guid)
                && entry.Property("Id").CurrentValue is Guid g && g == Guid.Empty)
            {
                entry.Property("Id").CurrentValue = Guid.NewGuid();
            }
        }

        await context.SaveChangesAsync(ct);
    }

    // A provider with an explicit surrogate id, one location carrying that provider id (shadow FK), and a
    // resource assigned to that location — so the location-provider join resolves the provider's projects.
    private async Task SeedProviderWithLocationAndResourceAsync(
        ProviderId providerId, string locationName, ProjectResourceId resourceId, int allocationUnitTypeId = 1)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(DomainProviderName.From($"prov-{Guid.NewGuid():N}"), countryId: 1);
        var location = LocationName.From(locationName);
        Assert.True(provider.AddLocation(location, $"https://{locationName}", ELocationType.HPC, isShared: true).IsSuccess);

        var resource = ProjectResource.Create(
            ResourceName.Create($"res-{Guid.NewGuid():N}"), provider, UserId.From(Guid.NewGuid()), Start, End);
        context.Add(resource);
        context.Entry(resource).Property("Id").CurrentValue = resourceId;

        Assert.True(provider.AssignResource(
            location, resource, allocationUnitTypeId, allocationUnitCount: null, description: null,
            aggregationName: null, specifications: NoSpecs, resourceHasNonSharedLocation: false,
            slug: $"asg-{locationName}").IsSuccess);

        context.Add(provider);
        context.Entry(provider).Property("Id").CurrentValue = providerId;

        // Point the just-added location at the provider id shadow FK.
        var locationEntry = context.ChangeTracker.Entries<Location>()
            .Single(e => e.State == EntityState.Added && e.Entity.Name.Equals(location));
        locationEntry.Property("ProviderId").CurrentValue = providerId;

        await SaveAsync(context, ct);
    }

    private async Task SeedLocationTypeAsync(ELocationType id)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var entity = (LocationType)Activator.CreateInstance(typeof(LocationType), nonPublic: true)!;
        typeof(LocationType).GetProperty(nameof(LocationType.Id))!.SetValue(entity, id);
        typeof(LocationType).GetProperty(nameof(LocationType.Name))!.SetValue(entity, id.ToString());
        context.Add(entity);
        await SaveAsync(context, ct);
    }

    private async Task<Core.Domain.Project.ProjectId> SeedProjectAsync(string shortName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var projectId = Core.Domain.Project.ProjectId.From(Guid.NewGuid());
        var project = ProjectAggregate.Create(
            new ProjectShortName(shortName),
            ProjectName.Create(shortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);
        context.Add(project);
        context.Entry(project).Property("Id").CurrentValue = projectId;

        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(++_domainTypeKeySeed);
        }

        await SaveAsync(context, ct);
        return projectId;
    }

    private async Task SeedResourceToProjectAsync(ProjectResourceId resourceId, Core.Domain.Project.ProjectId projectId)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var link = Core.Domain.Project.AssignedProjectResourceToProject.Create();
        context.Add(link);
        context.Entry(link).Property("ProjectId").CurrentValue = projectId;
        context.Entry(link).Property("ProjectResourceId").CurrentValue = resourceId;
        await SaveAsync(context, ct);
    }

    private async Task<Core.Domain.User.User> SeedUserAsync(string idpId, string email)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var user = Core.Domain.User.User.CreateUser(
            Email.Create(email), UserName.Create(idpId), new IdpId(idpId));
        context.Add(user);
        context.Entry(user).Property("Id").CurrentValue = UserId.New();
        await SaveAsync(context, ct);
        return user;
    }

    private async Task SeedDeletedUserAsync(string idpId, string email)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var user = Core.Domain.User.User.CreateUser(
            Email.Create(email), UserName.Create(idpId), new IdpId(idpId));
        user.Deactivate();
        context.Add(user);
        context.Entry(user).Property("Id").CurrentValue = UserId.New();
        await SaveAsync(context, ct);
    }

    // Assigns the user to each project with a project role (AssignedUserToProject + project-scoped
    // UserPermission). The user must be re-attached in this scope's context to reference it.
    private async Task AssignUserToProjectsWithRoleAsync(
        Core.Domain.User.User user, IReadOnlyList<Core.Domain.Project.ProjectId> projectIds, int roleId)
    {
        foreach (var projectId in projectIds)
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var ct = TestContext.Current.CancellationToken;
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

            var project = await context.Set<ProjectAggregate>()
                .IgnoreQueryFilters()
                .FirstAsync(p => EF.Property<ProjectId>(p, "Id") == projectId, ct);

            var attachedUser = await context.Set<User>()
                .FirstAsync(u => u.IdentityProviderId == user.IdentityProviderId, ct);

            Assert.True(project.AssignUserWithRole(attachedUser, UserId.New(), new ProjectRoleId(roleId)).IsSuccess);
            await SaveAsync(context, ct);
        }
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
