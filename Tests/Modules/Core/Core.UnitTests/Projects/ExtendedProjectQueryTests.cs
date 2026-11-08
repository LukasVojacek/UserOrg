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

namespace Core.UnitTests.Projects;

/// <summary>
/// <see cref="ProjectDetailsQuery.GetExtendedProjectAsync"/> executed against the SQLite relational provider, so
/// the staged reads (project, tags, resources, assignments joined to location, values) run as translated SQL
/// rather than through a mocked <see cref="IProjectQuery"/>.
/// </summary>
public sealed class ExtendedProjectQueryTests : SqliteQueryHarness
{
    private const string ShortName = "proj-ext";
    private const int DomainTypeKey = 42;
    private const int CoreHoursUnitTypeId = (int)EAllocationUnitType.CoreHours;
    private const string CoreHoursUnitTypeName = "core-hours";

    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IProjectQuery, ProjectDetailsQuery>();

    private protected override Task SeedAsync(DatabaseContext context) =>
        context.Database.ExecuteSqlAsync(
            $"INSERT INTO AllocationUnitType (Id, Name, Description) VALUES ({CoreHoursUnitTypeId}, {CoreHoursUnitTypeName}, NULL)");

    [Fact]
    public async Task Should_ReturnNull_When_ShortNameUnknown()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync(assign: null);

        var result = await QueryAsync(q => q.GetExtendedProjectAsync(new ProjectShortName("proj-missing"), ct));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ReturnProjectWithTagsAndNoResources_When_NoResourceAssigned()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync(assign: null, tag: "gpu");

        var result = await QueryAsync(q => q.GetExtendedProjectAsync(new ProjectShortName(ShortName), ct));

        Assert.NotNull(result);
        Assert.Equal(ShortName, result.ShortName.Value);
        Assert.Equal(ShortName, result.Name.Value);
        Assert.Equal(Start, result.StartDate);
        Assert.Equal(End, result.EndDate);
        Assert.Equal($"domain-{ShortName}", result.DomainTypeName.Value);
        Assert.Equal("gpu", Assert.Single(result.AssignedTags).Value);
        Assert.Empty(result.AssignedResources);
    }

    [Fact]
    public async Task Should_ReturnEmptyResources_When_OnlyResourceLinkIsSoftDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync(
            (provider, project) => AssignToLocation(provider, project, "hpc-res", "karolina", ELocationType.HPC, NoSpecs),
            removeResources: true);

        var result = await QueryAsync(q => q.GetExtendedProjectAsync(new ProjectShortName(ShortName), ct));

        Assert.NotNull(result);
        Assert.Empty(result.AssignedResources);
    }

    [Fact]
    public async Task Should_ReturnAssignmentWithHpcLocationType_When_ResourceAssignedToHpcLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync((provider, project) =>
            AssignToLocation(provider, project, "hpc-res", "karolina", ELocationType.HPC, [("CORES", "128")]));

        var result = await QueryAsync(q => q.GetExtendedProjectAsync(new ProjectShortName(ShortName), ct));

        Assert.NotNull(result);
        var assigned = Assert.Single(result.AssignedResources);
        Assert.Equal("hpc-res", assigned.ResourceName);
        Assert.Equal("hpc-res description", assigned.ResourceDescription);
        Assert.Equal("karolina", assigned.LocationName);
        Assert.Equal(ELocationType.HPC, assigned.LocationType);
        Assert.Equal(1000, assigned.AllocationAmount);
        Assert.Equal(EAllocationUnitType.CoreHours, assigned.AllocationUnitType);
        Assert.Equal("hpc-res-aggregation", assigned.AggregationName);
        Assert.Equal([("CORES", "128")], assigned.Specifications);
    }

    [Fact]
    public async Task Should_ReturnEachAssignmentWithItsOwnLocationType_When_ResourcesSpanLocationTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProjectAsync((provider, project) =>
        {
            AssignToLocation(provider, project, "hpc-res", "karolina", ELocationType.HPC, [("CORES", "128")]);
            AssignToLocation(provider, project, "vm-res", "openstack", ELocationType.CLOUD, NoSpecs);
        });

        var result = await QueryAsync(q => q.GetExtendedProjectAsync(new ProjectShortName(ShortName), ct));

        Assert.NotNull(result);
        var byResource = result.AssignedResources.ToDictionary(r => r.ResourceName);
        Assert.Equal(["hpc-res", "vm-res"], byResource.Keys.Order());
        Assert.Equal(ELocationType.HPC, byResource["hpc-res"].LocationType);
        Assert.Equal(ELocationType.CLOUD, byResource["vm-res"].LocationType);
        Assert.Equal("openstack", byResource["vm-res"].LocationName);
        Assert.Empty(byResource["vm-res"].Specifications);
    }

    private async Task<ExtendedProjectSummary?> QueryAsync(Func<IProjectQuery, Task<ExtendedProjectSummary?>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IProjectQuery>());
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
        Assert.True(provider.AddLocation(location, $"https://{locationName}", locationType, isShared: true).IsSuccess);

        var resource = ProjectResource.Create(
            ResourceName.Create(resourceName), provider, UserId.From(Guid.NewGuid()), Start, End, description: $"{resourceName} description");
        Assert.True(provider.AssignResource(
            location, resource, CoreHoursUnitTypeId, allocationUnitCount: 1000,
            description: null, aggregationName: $"{resourceName}-aggregation", specifications: specifications,
            resourceHasNonSharedLocation: false, slug: $"asg-{resourceName}").IsSuccess);
        Assert.True(project.AssignResource(resource).IsSuccess);
    }

    private async Task SeedProjectAsync(
        Action<ProviderAggregate, ProjectAggregate>? assign, string? tag = null, bool removeResources = false)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var project = ProjectAggregate.Create(
            new ProjectShortName(ShortName),
            ProjectName.Create(ShortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{ShortName}")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);

        if (tag is not null)
        {
            Assert.True(project.AssignExistingTag(ProjectTag.Create(TagName.Create(tag), UserName.Create("tagger"))).IsSuccess);
        }

        var provider = ProviderAggregate.Create(DomainProviderName.From("acme"), countryId: 1);
        assign?.Invoke(provider, project);

        context.Add(provider);
        context.Add(project);
        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(DomainTypeKey);
        }

        await context.SaveChangesAsync(ct);

        if (removeResources)
        {
            foreach (var resource in context.ChangeTracker.Entries<ProjectResource>().Select(e => e.Entity).ToList())
            {
                Assert.True(project.RemoveResource(resource).IsSuccess);
            }

            await context.SaveChangesAsync(ct);
        }
    }
}
