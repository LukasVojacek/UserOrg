using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;
using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests.UserInfo;

/// <summary>
/// <see cref="UserDetailsQuery.GetExtendedBySubAsync"/> executed against the SQLite relational provider, so the
/// membership walk (user, projects, resources, assignments joined to location and location type, values) runs as
/// translated SQL rather than through a mocked <see cref="IUserInfoQuery"/>.
/// </summary>
public sealed class ExtendedUserInfoQueryTests : SqliteQueryHarness
{
    private const string Sub = "sub-ext";
    private const string ShortName = "proj-ext";
    private const int CoreHoursUnitTypeId = (int)EAllocationUnitType.CoreHours;
    private const string CoreHoursUnitTypeName = "core-hours";

    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IUserInfoQuery, UserDetailsQuery>();

    private protected override async Task SeedAsync(DatabaseContext context)
    {
        var ct = TestContext.Current.CancellationToken;
        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO AllocationUnitType (Id, Name, Description) VALUES ({CoreHoursUnitTypeId}, {CoreHoursUnitTypeName}, NULL)", ct);
        foreach (var locationType in (ELocationType[])[ELocationType.HPC, ELocationType.CLOUD])
        {
            await context.Database.ExecuteSqlAsync(
                $"INSERT INTO LocationType (Id, Name, Description) VALUES ({(int)locationType}, {locationType.ToString()}, NULL)", ct);
        }
    }

    [Fact]
    public async Task Should_ReturnNull_When_SubUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUserAsync(membership: null);

        var result = await QueryAsync(q => q.GetExtendedBySubAsync(new IdpId("sub-missing"), ct));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ReturnUserWithoutResources_When_UserHasNoProject()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUserAsync(membership: null);

        var result = await QueryAsync(q => q.GetExtendedBySubAsync(new IdpId(Sub), ct));

        Assert.NotNull(result);
        Assert.Equal(Sub, result.IdentityProviderId.Value);
        Assert.Equal($"{Sub}@example.invalid", result.Email.Value);
        Assert.Equal(Sub, result.UserName.Value);
        Assert.Empty(result.ProjectResources);
    }

    [Fact]
    public async Task Should_ReturnUserWithoutResources_When_ProjectHasNoResource()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUserAsync(membership: (_, _) => { });

        var result = await QueryAsync(q => q.GetExtendedBySubAsync(new IdpId(Sub), ct));

        Assert.NotNull(result);
        Assert.Equal(Sub, result.IdentityProviderId.Value);
        Assert.Empty(result.ProjectResources);
    }

    [Fact]
    public async Task Should_ReturnResourceWithHpcLocation_When_MembershipReachesAssignedResource()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUserAsync((provider, project) =>
            AssignToLocation(provider, project, "hpc-res", "karolina", ELocationType.HPC, [("CORES", "128")]));

        var result = await QueryAsync(q => q.GetExtendedBySubAsync(new IdpId(Sub), ct));

        Assert.NotNull(result);
        var resource = Assert.Single(result.ProjectResources);
        Assert.Equal(ShortName, resource.ProjectShortName.Value);
        Assert.Equal("hpc-res", resource.ResourceName);
        Assert.Equal("hpc-res description", resource.Description);
        Assert.Equal(Start, resource.StartDate);
        Assert.Equal(End, resource.EndDate);

        var location = Assert.Single(resource.Locations);
        Assert.Equal("karolina", location.LocationName);
        Assert.Equal("karolina location", location.Description);
        Assert.Equal("https://karolina", location.Endpoint);
        Assert.False(location.IsPrivate);
        Assert.Equal("HPC", location.LocationType);
        Assert.Equal(1000, location.AllocationUnitCount);
        Assert.Equal(EAllocationUnitType.CoreHours, location.AllocationUnitType);
        Assert.Equal([("CORES", "128")], location.Values);
    }

    [Fact]
    public async Task Should_ReturnEachLocationWithItsOwnTypeName_When_ResourcesSpanLocationTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedUserAsync((provider, project) =>
        {
            AssignToLocation(provider, project, "hpc-res", "karolina", ELocationType.HPC, [("CORES", "128")]);
            AssignToLocation(provider, project, "vm-res", "openstack", ELocationType.CLOUD, NoSpecs);
        });

        var result = await QueryAsync(q => q.GetExtendedBySubAsync(new IdpId(Sub), ct));

        Assert.NotNull(result);
        var byResource = result.ProjectResources.ToDictionary(r => r.ResourceName);
        Assert.Equal(["hpc-res", "vm-res"], byResource.Keys.Order());
        Assert.Equal("HPC", Assert.Single(byResource["hpc-res"].Locations).LocationType);
        var cloud = Assert.Single(byResource["vm-res"].Locations);
        Assert.Equal("CLOUD", cloud.LocationType);
        Assert.Equal("openstack", cloud.LocationName);
        Assert.Empty(cloud.Values);
    }

    private async Task<UserExtendedDetails?> QueryAsync(Func<IUserInfoQuery, Task<UserExtendedDetails?>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IUserInfoQuery>());
    }

    private static void AssignToLocation(
        ProviderAggregate provider,
        ProjectAggregate project,
        string resourceName,
        string locationName,
        ELocationType locationType,
        (string Name, string Value)[] specifications)
    {
        var location = LocationName.From(locationName);
        Assert.True(provider.AddLocation(
            location, $"https://{locationName}", locationType, isPrivate: false, isShared: true,
            description: $"{locationName} location").IsSuccess);

        var resource = ProjectResource.Create(
            ResourceName.Create(resourceName), provider, UserId.From(Guid.NewGuid()), Start, End, description: $"{resourceName} description");
        Assert.True(provider.AssignResource(
            location, resource, CoreHoursUnitTypeId, allocationUnitCount: 1000,
            description: null, aggregationName: null, specifications: specifications,
            resourceHasNonSharedLocation: false, slug: $"asg-{resourceName}").IsSuccess);
        Assert.True(project.AssignResource(resource).IsSuccess);
    }

    private async Task SeedUserAsync(Action<ProviderAggregate, ProjectAggregate>? membership)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var user = UserAggregate.CreateUser(Email.Create($"{Sub}@example.invalid"), UserName.Create(Sub), new IdpId(Sub));
        context.Add(user);

        if (membership is not null)
        {
            var project = ProjectAggregate.Create(
                new ProjectShortName(ShortName),
                ProjectName.Create(ShortName),
                ProjectDomainType.FromName(DomainTypeName.Create($"domain-{ShortName}")),
                UserId.New(),
                Start,
                End,
                ServiceName.Default);
            Assert.True(project.AssignUser(user, UserId.New()).IsSuccess);

            var provider = ProviderAggregate.Create(DomainProviderName.From("acme"), countryId: 1);
            membership(provider, project);

            context.Add(provider);
            context.Add(project);
            foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>())
            {
                var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
                domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(1);
            }
        }

        await context.SaveChangesAsync(ct);
    }
}
