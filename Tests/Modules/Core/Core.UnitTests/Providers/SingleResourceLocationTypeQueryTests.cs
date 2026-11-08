using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// The single-resource reads of <see cref="ResourceDetailsQuery"/> (by id, by name, by provider and name) executed
/// against the SQLite relational provider. None of them passes a location type, so a resource assigned to locations
/// of several types comes back with every assignment and each assignment's own type.
/// </summary>
public sealed class SingleResourceLocationTypeQueryTests : SqliteQueryHarness
{
    private const string ProviderNameValue = "acme";
    private const string ResourceNameValue = "mixed-res";
    private const string ShortName = "proj-mixed";

    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly (string LocationName, ELocationType LocationType)[] Locations =
        [("karolina", ELocationType.HPC), ("openstack", ELocationType.CLOUD)];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IResourceQuery, ResourceDetailsQuery>();

    [Fact]
    public async Task Should_ReturnEveryAssignmentWithItsLocationType_When_ReadById()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await SeedMixedResourceAsync();

        var result = await ResourceAsync(q => q.GetResourceAsync(id, [new ProjectShortName(ShortName)], ct));

        AssertMixedAssignments(result);
    }

    [Fact]
    public async Task Should_ReturnEveryAssignmentWithItsLocationType_When_ReadByName()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedMixedResourceAsync();

        var result = await ResourceAsync(q => q.GetResourceByNameAsync(
            ResourceName.Create(ResourceNameValue), [new ProjectShortName(ShortName)], ct));

        AssertMixedAssignments(result);
    }

    [Fact]
    public async Task Should_ReturnEveryAssignmentWithItsLocationType_When_ReadByProviderAndName()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedMixedResourceAsync();

        var result = await ResourceAsync(q => q.GetResourceByProviderAndNameAsync(
            DomainProviderName.From(ProviderNameValue), ResourceName.Create(ResourceNameValue), null, ct));

        AssertMixedAssignments(result);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ReadByIdOfUnknownResource()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedMixedResourceAsync();

        var result = await ResourceAsync(q => q.GetResourceAsync(Guid.NewGuid(), [new ProjectShortName(ShortName)], ct));

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_ReturnNull_When_ReadByIdWithEmptyScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await SeedMixedResourceAsync();

        var result = await ResourceAsync(q => q.GetResourceAsync(id, [], ct));

        Assert.Null(result);
    }

    private static void AssertMixedAssignments(ResourceSummary? result)
    {
        Assert.NotNull(result);
        Assert.Equal(ResourceNameValue, result.Name);
        Assert.Equal(
            Locations,
            result.Assignments.Select(a => (a.LocationName, a.LocationType)).OrderBy(a => a.LocationName));
    }

    private async Task<ResourceSummary?> ResourceAsync(Func<IResourceQuery, Task<ResourceSummary?>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IResourceQuery>());
    }

    private async Task<Guid> SeedMixedResourceAsync()
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var userIdp = IdpId.From(Guid.NewGuid().ToString());
        var user = User.CreateUser(Email.Create("no@example.org"), UserName.Create("test-user"), userIdp);
        context.Add(user);
        await context.SaveChangesAsync(ct);

        var userid = await context.Users.Where(f => f.IdentityProviderId == userIdp)
            .Select(s => EF.Property<UserId>(s, "Id"))
            .FirstAsync();

        var provider = ProviderAggregate.Create(DomainProviderName.From(ProviderNameValue), countryId: 1);
        var resource = ProjectResource.Create(ResourceName.Create(ResourceNameValue), provider, userid, Start, End);
        foreach (var (locationName, locationType) in Locations)
        {
            var location = LocationName.From(locationName);
            Assert.True(provider.AddLocation(location, $"https://{locationName}", locationType, isShared: true).IsSuccess);
            Assert.True(provider.AssignResource(
                location, resource, allocationUnitTypeId: 1, allocationUnitCount: null,
                description: null, aggregationName: null, specifications: NoSpecs,
                resourceHasNonSharedLocation: false, slug: $"asg-{locationName}").IsSuccess);
        }

        var project = ProjectAggregate.Create(
            new ProjectShortName(ShortName),
            ProjectName.Create(ShortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{ShortName}")),
            userid,
            Start,
            End,
            ServiceName.Default);
        Assert.True(project.AssignResource(resource).IsSuccess);

        context.Add(provider);
        context.Add(resource);
        context.Add(project);
        foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>())
        {
            var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(1);
        }

        await context.SaveChangesAsync(ct);

        return ((ProjectResourceId)context.Entry(resource).Property("Id").CurrentValue!).Value;
    }
}
