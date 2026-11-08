using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using DomainProviderName = Core.Domain.Provider.ProviderName;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// <see cref="ResourceDetailsQuery"/>'s <c>ProviderResourceName</c> projection — the resource's natural key
/// (ADR-0044 §1) rendered for the wire as <c>"&lt;provider&gt;|&lt;resource&gt;"</c>.
///
/// Run on the SQLite relational provider rather than EF InMemory, which is what
/// <see cref="ResourceDetailsQueryTests"/> uses. The owning provider is reachable only through the private
/// <c>_provider</c> navigation behind the shadow <c>ProviderId</c> FK, so a projection that reads the domain's
/// computed <c>ProjectResource.ProviderName</c> has no SQL translation: InMemory evaluates the whole query
/// client-side and hides that, while a relational provider materialises the resource row <b>without</b> the
/// provider join and the field silently comes back null for every row. These tests therefore only mean
/// something against real SQL. The harness follows <c>InitializeProjectResourceHandlerPersistenceTests</c>.
/// </summary>
public sealed class ResourceProviderNameProjectionTests : SqliteQueryHarness
{
    private const string ProviderNameValue = "acme";
    private const string OwnedResourceName = "gpu-res";
    private const string LegacyResourceName = "legacy-res";

    private static readonly (string Name, string Value)[] NoSpecs = [];
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IResourceQuery, ResourceDetailsQuery>();

    [Fact]
    public async Task Should_ProjectProviderQualifiedName_When_ListingResources()
    {
        // The regression: against real SQL the field must carry the owning provider, not null.
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync();

        var page = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 50, ct));

        var owned = Assert.Single(page.Items, r => r.Name == OwnedResourceName);
        Assert.Equal($"{ProviderNameValue}|{OwnedResourceName}", owned.ProviderResourceName);
    }

    [Fact]
    public async Task Should_ProjectProviderQualifiedName_When_ReadingOneByProviderAndName()
    {
        // The single-resource read goes through LoadAsync, a projection separate from the paged list's, so both
        // are pinned: fixing one and leaving the other is exactly the shape of the original defect.
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync();

        var resource = await ResourceAsync(q => q.GetResourceByProviderAndNameAsync(
            DomainProviderName.From(ProviderNameValue), ResourceName.Create(OwnedResourceName), null, ct));

        Assert.NotNull(resource);
        Assert.Equal($"{ProviderNameValue}|{OwnedResourceName}", resource!.ProviderResourceName);
    }

    [Fact]
    public async Task Should_ProjectNull_When_ResourceIsLegacyStandaloneRow()
    {
        // A legacy row carries no owning provider (ProviderId IS NULL), so it has no per-provider natural key
        // (ProjectResource.Key throws for it) and the wire value is null rather than a half-formed "|name".
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync();

        var page = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 50, ct));

        var legacy = Assert.Single(page.Items, r => r.Name == LegacyResourceName);
        Assert.Null(legacy.ProviderResourceName);
    }

    [Fact]
    public async Task Should_QualifyWithOwningProvider_When_SameResourceNameExistsUnderTwoProviders()
    {
        // ADR-0026: the name alone is not unique across providers, which is the whole reason the field exists.
        // Two rows named "shared-res" under different providers must come back distinctly qualified.
        var ct = TestContext.Current.CancellationToken;
        await SeedSameNameUnderTwoProvidersAsync("shared-res", "alpha", "beta");

        var page = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 50, ct));

        Assert.Equal(
            ["alpha|shared-res", "beta|shared-res"],
            page.Items.Select(r => r.ProviderResourceName).Order());
    }

    [Fact]
    public async Task Should_KeepPagingCardinality_When_ProviderIsOuterJoined()
    {
        // The name is read through a LEFT JOIN inside the paged read, so the join must not multiply rows:
        // Provider is the principal of a many-to-one, which keeps it 1:1, and TotalItems/Skip/Take are
        // computed over exactly that joined query. Pin it against real SQL — an inflated total or a row
        // repeated across pages is the failure mode a one-page in-memory test would not show.
        var ct = TestContext.Current.CancellationToken;
        await SeedManyUnderOneProviderAsync("alpha", "bravo", "charlie");

        var pageOne = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 1, 2, ct));
        var pageTwo = await QueryAsync(q => q.GetResourcesAsync(new ResourceFilter(null, null, null, null, null), 2, 2, ct));

        Assert.Equal(3, pageOne.TotalItems);
        Assert.Equal(3, pageTwo.TotalItems);
        Assert.Equal(2, pageOne.Items.Count);
        Assert.Single(pageTwo.Items);

        // The two pages are a partition: no row repeats, none goes missing, and every one is qualified.
        var seen = pageOne.Items.Concat(pageTwo.Items).Select(r => r.ProviderResourceName).ToList();
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(
            [$"{ProviderNameValue}|alpha", $"{ProviderNameValue}|bravo", $"{ProviderNameValue}|charlie"],
            seen.Order());
    }

    private async Task<QueryPage<ResourceSummary>> QueryAsync(Func<IResourceQuery, Task<QueryPage<ResourceSummary>>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IResourceQuery>());
    }

    private async Task<ResourceSummary?> ResourceAsync(Func<IResourceQuery, Task<ResourceSummary?>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IResourceQuery>());
    }

    // One provider-owned resource plus one legacy standalone row. The legacy row is created through the normal
    // factory (which requires a provider) and then has its shadow FK cleared in SQL — the domain offers no way
    // to build a provider-less resource, and that is exactly the pre-v2 shape the read must still tolerate.
    private async Task SeedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var userIdp = IdpId.From(Guid.NewGuid().ToString());
        var user = User.CreateUser(Email.Create("no@example.org"), UserName.Create("test-user"), userIdp);
        context.Add(user);
        await context.SaveChangesAsync(ct);

        var userid = await context.Users.Where(f => f.IdentityProviderId == userIdp)
            .Select(s => EF.Property<UserId>(s, "Id"))
            .FirstAsync();

        var provider = ProviderAggregate.Create(DomainProviderName.From(ProviderNameValue), countryId: 1);
        var location = LocationName.From("loc-1");
        Assert.True(provider.AddLocation(location, "https://loc-1", ELocationType.HPC, isShared: true).IsSuccess);

        var owned = ProjectResource.Create(ResourceName.Create(OwnedResourceName), provider, userid, Start, End);
        // The provider-qualified read narrows by provider through its ACTIVE assignments, so the owned resource
        // needs one to be reachable at all — the projection is what is under test, not the scoping.
        Assert.True(provider.AssignResource(
            location, owned, allocationUnitTypeId: 1, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: NoSpecs,
            resourceHasNonSharedLocation: false).IsSuccess);

        context.Add(provider);
        context.Add(owned);
        context.Add(ProjectResource.Create(ResourceName.Create(LegacyResourceName), provider, userid, Start, End));
        await context.SaveChangesAsync(ct);

        await context.Database.ExecuteSqlRawAsync(
            "UPDATE ProjectResource SET ProviderId = NULL WHERE Name = {0}", [LegacyResourceName], ct);
    }

    private async Task SeedSameNameUnderTwoProvidersAsync(string resourceName, params string[] providerNames)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var userIdp = IdpId.From(Guid.NewGuid().ToString());
        var user = User.CreateUser(Email.Create("no@example.org"), UserName.Create("test-user"), userIdp);
        context.Add(user);
        await context.SaveChangesAsync(ct);

        var userid = await context.Users.Where(f => f.IdentityProviderId == userIdp)
            .Select(s => EF.Property<UserId>(s, "Id"))
            .FirstAsync();

        foreach (var providerName in providerNames)
        {
            var provider = ProviderAggregate.Create(DomainProviderName.From(providerName), countryId: 1);
            context.Add(provider);
            context.Add(ProjectResource.Create(ResourceName.Create(resourceName), provider, userid, Start, End));
        }

        await context.SaveChangesAsync(ct);
    }

    // Several resources under one provider — the fixture for the paging-cardinality guard.
    private async Task SeedManyUnderOneProviderAsync(params string[] resourceNames)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var userIdp = IdpId.From(Guid.NewGuid().ToString());
        var user = User.CreateUser(Email.Create("no@example.org"), UserName.Create("test-user"), userIdp);
        context.Add(user);
        await context.SaveChangesAsync(ct);

        var userid = await context.Users.Where(f => f.IdentityProviderId == userIdp)
            .Select(s => EF.Property<UserId>(s, "Id"))
            .FirstAsync();

        var provider = ProviderAggregate.Create(DomainProviderName.From(ProviderNameValue), countryId: 1);
        context.Add(provider);
        foreach (var resourceName in resourceNames)
        {
            context.Add(ProjectResource.Create(ResourceName.Create(resourceName), provider, userid, Start, End));
        }

        await context.SaveChangesAsync(ct);
    }
}
