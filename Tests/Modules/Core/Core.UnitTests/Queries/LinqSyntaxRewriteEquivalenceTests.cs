using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProviderAggregate = Core.Domain.Provider.Provider;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests.Queries;

/// <summary>
/// Equivalence guard for the query-syntax → method-syntax rewrite of the read queries in
/// <c>Core.DataAccess.Queries</c>. Each case pairs the ORIGINAL query-syntax composer with the
/// composer now in production and asserts the two build the *same* LINQ expression tree.
///
/// The <see cref="IDataQuery{T}"/> API takes a plain <c>Func&lt;IQueryable&lt;T&gt;, …&gt;</c>, so a
/// composer contributes nothing but the operator chain it appends: identical trees mean identical
/// EF translation and therefore identical SQL and results, independently of any seeded data. That is
/// a stronger statement than comparing rows for one fixture, and it is what these tests assert.
/// </summary>
public sealed class LinqSyntaxRewriteEquivalenceTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public LinqSyntaxRewriteEquivalenceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        _serviceProvider = services.BuildServiceProvider();
    }

    // Mirrors of the queries' private row records — only the shape matters, and it is identical on
    // both sides of every pair.
    private sealed record UserRow(UserId UserId, IdpId IdentityProviderId, Email Email, UserName UserName);
    private sealed record ResourceScalarRow(
        Guid Id, string Name, ProviderName? ProviderName, string? Description, Email? Email,
        DateTimeOffset StartDate, DateTimeOffset EndDate, EOperationMode Mode, string? LicenseUrl,
        EProjectResourceState State);

    private IQueryable<T> Set<T>() where T : class
        => _serviceProvider.GetRequiredService<DatabaseContext>().Set<T>().AsNoTracking();

    private static void AssertSameTree<TResult>(IQueryable<TResult> queryForm, IQueryable<TResult> methodForm)
        => Assert.Equal(queryForm.Expression.ToString(), methodForm.Expression.ToString());

    [Fact]
    public void Should_BuildSameTree_When_GetLocationsRewritten()
    {
        // LocationDetailsQuery.GetLocationsAsync (base data) — bare projection, no predicate.
        var locs = Set<Location>();

        AssertSameTree(
            from loc in locs
            select new LocationBaseData(
                loc.Name.ToString(),
                loc.Endpoint),

            locs
                .Select(loc => new LocationBaseData(
                    loc.Name.ToString(),
                    loc.Endpoint)));
    }

    [Fact]
    public void Should_BuildSameTree_When_ResolveProviderIdRewritten()
    {
        // The GetProviderIdByName composer, duplicated in LocationDetailsQuery and ProjectDetailsQuery.
        var providerName = ProviderName.Create("acme");
        var providers = Set<ProviderAggregate>();

        AssertSameTree(
            from prov in providers
            where prov.Name == providerName
            select (ProviderId?)EF.Property<ProviderId>(prov, "Id"),

            providers
                .Where(prov => prov.Name == providerName)
                .Select(prov => (ProviderId?)EF.Property<ProviderId>(prov, "Id")));
    }

    [Fact]
    public void Should_BuildSameTree_When_ProjectUserDetailsRewritten()
    {
        // ProjectDetailsQuery — User rows read through IgnoreQueryFilters() by shadow PK.
        var pageUserIds = new List<UserId>();
        var users = Set<UserAggregate>();

        AssertSameTree(
            from u in users.IgnoreQueryFilters()
            where pageUserIds.Contains(EF.Property<UserId>(u, "Id"))
            select new UserRow(
                EF.Property<UserId>(u, "Id"),
                u.IdentityProviderId,
                u.Email,
                u.UserName),

            users.IgnoreQueryFilters()
                .Where(u => pageUserIds.Contains(EF.Property<UserId>(u, "Id")))
                .Select(u => new UserRow(
                    EF.Property<UserId>(u, "Id"),
                    u.IdentityProviderId,
                    u.Email,
                    u.UserName)));
    }

    [Fact]
    public void Should_BuildSameTree_When_ProviderDetailRewritten()
    {
        // ProviderDetailsQuery — projection carrying a correlated sub-collection over a shadow FK.
        var name = ProviderName.Create("acme");
        var providers = Set<ProviderAggregate>();
        var locations = Set<Location>();
        var countries = Set<Country>();

        AssertSameTree(
            from p in providers
            where p.Name == name
            select new ProviderDetail(
                p.Name,
                p.Description,
                p.SupportEmail,
                p.ExternalId,
                p.Mode,
                p.ServiceName,
                p.IsPrivate,
                (from c in countries where c.Id == p.CountryId select c.Name).First().Value,
                p.IsDeleted,
                (from loc in locations
                 where EF.Property<ProviderId>(loc, "ProviderId") == EF.Property<ProviderId>(p, "Id")
                 select new ProviderLocationDetail(
                     loc.Name,
                     loc.Endpoint,
                     loc.Description,
                     loc.IsPrivate,
                     loc.IsShared)).ToList()),

            providers
                .Where(p => p.Name == name)
                .Select(p => new ProviderDetail(
                    p.Name,
                    p.Description,
                    p.SupportEmail,
                    p.ExternalId,
                    p.Mode,
                    p.ServiceName,
                    p.IsPrivate,
                    countries.Where(c => c.Id == p.CountryId).Select(c => c.Name).First().Value,
                    p.IsDeleted,
                    locations
                        .Where(loc => EF.Property<ProviderId>(loc, "ProviderId") == EF.Property<ProviderId>(p, "Id"))
                        .Select(loc => new ProviderLocationDetail(
                            loc.Name,
                            loc.Endpoint,
                            loc.Description,
                            loc.IsPrivate,
                            loc.IsShared))
                        .ToList())));
    }

    [Fact]
    public void Should_BuildSameTree_When_PagedResourceScalarsRewritten()
    {
        // ResourceDetailsQuery stage 0 — the paged read. `orderby a, b` must become OrderBy/ThenBy
        // placed between Where and Select, or paging order changes.
        List<ProjectResourceId>? candidateResourceIds = null;
        DateTimeOffset? startDate = null;
        DateTimeOffset? endDate = null;
        var projectResources = Set<ProjectResource>();

        AssertSameTree(
            from pr in projectResources
            where (candidateResourceIds == null || candidateResourceIds.Contains(EF.Property<ProjectResourceId>(pr, "Id")))
                && (startDate == null || pr.StartDate >= startDate)
                && (endDate == null || pr.EndDate <= endDate)
            orderby pr.ResourceName, EF.Property<ProjectResourceId>(pr, "Id")
            select new ResourceScalarRow(
                EF.Property<ProjectResourceId>(pr, "Id").Value,
                pr.ResourceName.Value,
                pr.ProviderName,
                pr.Description,
                pr.PrimaryInvestigatorEmail,
                pr.StartDate,
                pr.EndDate,
                pr.OperationMode,
                pr.LicenseUrl,
                pr.State),

            projectResources
                .Where(pr => (candidateResourceIds == null || candidateResourceIds.Contains(EF.Property<ProjectResourceId>(pr, "Id")))
                    && (startDate == null || pr.StartDate >= startDate)
                    && (endDate == null || pr.EndDate <= endDate))
                .OrderBy(pr => pr.ResourceName)
                .ThenBy(pr => EF.Property<ProjectResourceId>(pr, "Id"))
                .Select(pr => new ResourceScalarRow(
                    EF.Property<ProjectResourceId>(pr, "Id").Value,
                    pr.ResourceName.Value,
                    pr.ProviderName,
                    pr.Description,
                    pr.PrimaryInvestigatorEmail,
                    pr.StartDate,
                    pr.EndDate,
                    pr.OperationMode,
                    pr.LicenseUrl,
                    pr.State)));
    }

    [Fact]
    public void Should_BuildSameTree_When_ResourceScalarsByFilterRewritten()
    {
        // ResourceDetailsQuery stage A — the unpaged by-id / by-name variant.
        List<ProjectResourceId>? candidateResourceIds = null;
        var hasId = false;
        var hasName = false;
        ProjectResourceId targetId = default;
        ResourceName targetName = default;
        DateTimeOffset? startDate = null;
        DateTimeOffset? endDate = null;
        var projectResources = Set<ProjectResource>();

        AssertSameTree(
            from pr in projectResources
            where (candidateResourceIds == null || candidateResourceIds.Contains(EF.Property<ProjectResourceId>(pr, "Id")))
                && (!hasId || EF.Property<ProjectResourceId>(pr, "Id") == targetId)
                && (!hasName || pr.ResourceName == targetName)
                && (startDate == null || pr.StartDate >= startDate)
                && (endDate == null || pr.EndDate <= endDate)
            select new ResourceScalarRow(
                EF.Property<ProjectResourceId>(pr, "Id").Value,
                pr.ResourceName.Value,
                pr.ProviderName,
                pr.Description,
                pr.PrimaryInvestigatorEmail,
                pr.StartDate,
                pr.EndDate,
                pr.OperationMode,
                pr.LicenseUrl,
                pr.State),

            projectResources
                .Where(pr => (candidateResourceIds == null || candidateResourceIds.Contains(EF.Property<ProjectResourceId>(pr, "Id")))
                    && (!hasId || EF.Property<ProjectResourceId>(pr, "Id") == targetId)
                    && (!hasName || pr.ResourceName == targetName)
                    && (startDate == null || pr.StartDate >= startDate)
                    && (endDate == null || pr.EndDate <= endDate))
                .Select(pr => new ResourceScalarRow(
                    EF.Property<ProjectResourceId>(pr, "Id").Value,
                    pr.ResourceName.Value,
                    pr.ProviderName,
                    pr.Description,
                    pr.PrimaryInvestigatorEmail,
                    pr.StartDate,
                    pr.EndDate,
                    pr.OperationMode,
                    pr.LicenseUrl,
                    pr.State)));
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
