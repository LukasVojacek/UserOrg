using System.Text.Json;
using System.Text.Json.Serialization;

using Core.DataAccess.Contexts;
using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.ResourceAssignment;
using Core.UnitTests.Features.Resource.Workflow.Handlers;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using ELocationType = Core.Domain.Shared.ELocationType;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Persistence-level tests for the three resource-assignment write endpoints, run against the real
/// <see cref="DatabaseContext"/> on SQLite. The existing <c>ResourceAssignmentEndpointTests</c> drive the same
/// handlers over Moq doubles, so the unit of work is a verify-only mock and no SQL is ever emitted — which is
/// exactly the blind spot T-055 lives in: the defect is in the state EF assigns to a newly created child of an
/// already-tracked aggregate, and that state is invisible without a real change tracker and a real database.
///
/// <b>The claim under test (T-055-08).</b> <see cref="AssignResourceEndpoint"/> loads the provider tracked via
/// <c>IProviderRepository.FindAsync</c>, calls <c>provider.AssignResource(...)</c> and then
/// <c>unitOfWork.SaveChangesAsync()</c>. The new <c>AssignedLocation</c> is discovered by <c>DetectChanges</c>
/// as a new element of a TRACKED <c>Location</c> collection, and EF's graph-attach heuristic there
/// (<c>NavigationFixer</c> → <c>AttachGraph(Added, Modified, forceStateWhenUnknownKey: false)</c>) picks
/// <see cref="EntityState.Modified"/> over <see cref="EntityState.Added"/> whenever the key is already set.
/// <c>AssignedLocation.Create</c> client-generates <c>Id</c> (AssignedLocation.cs:80), so the key IS set and the
/// save emits <c>UPDATE "AssignedLocation" SET ... WHERE "Id" = &lt;fresh uuid v7&gt;</c>, which matches no row.
/// Unlike the workflow handler this endpoint has no detached graph anywhere, so nothing force-attaches the
/// subtree and nothing rescues it.
///
/// The reproductions pin the required END STATE (exactly one new allocation row, its values, no duplicate
/// provider or location), not a route and not a particular exception, so the fix cannot trade one failure for
/// another.
///
/// The harness follows <c>InitializeProjectResourceHandlerPersistenceTests</c>: schema generated from the real
/// model with two mechanical DDL rewrites, and a test-local <see cref="DbContextUnitOfWork"/> — the real
/// <c>IUnitOfWork</c> routes the durable <c>ResourceAssignmentChangedEvent</c> raised by
/// <c>Provider.AssignResource</c> into <c>OutboxDbContext</c>, whose options hardcode <c>UseSqlServer</c>
/// (DatabaseContext.UnitOfWork.cs, GetOutboxOptions) and die under SQLite with <c>near "SAVE": syntax error</c>
/// before any assertion runs.
/// </summary>
public sealed class AssignResourceEndpointPersistenceTests : SqliteQueryHarness
{
    private const string ProviderNameValue = "acme";
    private const string LocationNameValue = "location-1";

    /// <summary>Already allocated to the location by the seed — the row the new INSERT must not disturb.</summary>
    private const string SeededResourceName = "seeded-resource";

    /// <summary>Unallocated in the seed; the resource the endpoint under test assigns.</summary>
    private const string AssignedResourceName = "assign-me";

    private const string SeededSlug = "seeded-allocation-slug";
    private const int NodeHours = (int)EAllocationUnitType.NodeHours;

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    // The seeded allocation carries two specifications so the replace-all path of the PUT endpoint has
    // something to replace, and so the POST assertions can distinguish "the new allocation's values" from
    // "every value row in the database".
    private static readonly (string Name, string Value)[] SeededSpecs = [("seed-a", "1"), ("seed-b", "2")];

    protected override void AddServices(IServiceCollection services)
    {
        // The production registration shape (Core.DataAccess/ServiceCollectionExtensions.cs): the same scoped
        // DatabaseContext behind every write port, so the aggregate the endpoint mutates is tracked by the very
        // context that saves it — which is precisely the condition that triggers the defect.
        services.AddScoped<IUnitOfWork>(sp => new DbContextUnitOfWork(sp.GetRequiredService<DatabaseContext>()));
        services.AddScoped<IProviderRepository>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IRepository<ProviderAggregate, ProviderName>>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IRepository<ProjectResource, ProjectResourceKey>>(sp => sp.GetRequiredService<DatabaseContext>());
    }

    // ── Characterization: the fixture and the change-tracker mechanism ────────────────────────────────

    [Fact]
    public async Task Should_SeedOneProviderOneSharedLocationTwoResourcesAndOneAllocation()
    {
        // Pins the fixture every reproduction below rests on. The location is SHARED, so the domain permits a
        // second allocation on it — without that the endpoint would fail on an invariant and never reach the
        // save, and the tests would prove nothing about persistence.
        await SeedAsync();

        var counts = await CountRowsAsync();

        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(2, counts.Resources);
        Assert.Equal(1, counts.AssignedLocations);
        Assert.Equal(2, await AllocationValueCountAsync());
    }

    [Fact]
    public async Task Should_MarkTheNewAllocationModified_When_CreatedUnderATrackedProvider()
    {
        // The mechanism, pinned as observable behaviour rather than prose. This is the change tracker's verdict
        // on the domain call alone — it stays true after the fix, because the fix does not change what
        // DetectChanges concludes; it adds an explicit repository call (IProviderRepository
        // .MarkAssignedLocationAsAdded → DbContext.Add) that overrides the conclusion for the whole subtree.
        //
        // Three facts, in one place:
        //   * the parent Location is tracked Unchanged — there is no detached graph here to force-attach;
        //   * the new AssignedLocation has its key already set (client-generated uuid v7), which is what the
        //     graph-attach heuristic branches on;
        //   * so it comes out Modified — an UPDATE against a row that does not exist — while its
        //     AssignedLocationValue children, whose keys EF mints at attach time, come out Added. Parent and
        //     children disagree, so the write is torn as well as wrong.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();

        var provider = await repository.FindAsync(ProviderName.From(ProviderNameValue), TestContext.Current.CancellationToken);
        Assert.NotNull(provider);
        var location = Assert.Single(provider.Locations);
        Assert.Equal(EntityState.Unchanged, context.Entry(provider).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(location).State);

        var resource = await LoadResourceAsync(scope, AssignedResourceName);
        var assignment = provider.AssignResource(
            LocationName.From(LocationNameValue), resource, NodeHours, allocationUnitCount: 100,
            description: "d", aggregationName: null,
            specifications: [("cpu", "16"), ("mem", "64")],
            resourceHasNonSharedLocation: false, slug: AllocationSlug.New(resource.ResourceName));
        Assert.True(assignment.IsSuccess);

        context.ChangeTracker.DetectChanges();
        var allocation = assignment.Value;

        Assert.NotEqual(Guid.Empty, allocation.Id);
        Assert.True(context.Entry(allocation).IsKeySet);
        Assert.Equal(EntityState.Modified, context.Entry(allocation).State);
        Assert.All(allocation.Values, value => Assert.Equal(EntityState.Added, context.Entry(value).State));
    }

    // ── Reproduction: POST assign (red until the endpoint attaches the new subtree as Added) ──────────

    [Fact]
    public async Task Should_InsertExactlyOneNewAllocationWithItsValues_When_AssigningThroughTheEndpoint()
    {
        // THE reproduction. The real endpoint, the real repositories, the real DatabaseContext, a real database.
        // Today the save emits UPDATE "AssignedLocation" ... WHERE "Id" = <fresh uuid v7> and dies with
        // DbUpdateConcurrencyException before any of these assertions is reached.
        //
        // The assertions describe the END STATE the caller is entitled to, not the route taken to it: the new
        // allocation row exists ALONGSIDE the seeded one, carries the slug the endpoint reported in its 201,
        // owns exactly its own two specification rows, hangs off the resource that was actually assigned, and
        // no Provider or Location row was duplicated on the way.
        await SeedAsync();

        var result = await AssignAsync([new("cpu", "16"), new("mem", "64")]);

        var created = Assert.IsType<Created<AssignResourceEndpoint.Response>>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var newSlug = created.Value!.Slug;

        var counts = await CountRowsAsync();
        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(2, counts.Resources);
        Assert.Equal(2, counts.AssignedLocations);

        // A bare row count would still pass if the save had overwritten the seeded allocation instead of
        // inserting a second one — which is exactly what the UPDATE it emits today would do if the client-
        // generated key ever collided.
        // Both sides ordered: the minted slug sorts before the seeded one, and the point is the SET.
        Assert.Equal(new[] { SeededSlug, newSlug }.Order(), await AllocationSlugsAsync());

        // Parent and children must agree that the allocation is new: two seeded values plus the two supplied
        // here, with both of the new ones hanging off the new allocation.
        Assert.Equal(4, await AllocationValueCountAsync());
        Assert.Equal(2, await AllocationValueCountAsync(newSlug));

        // The new allocation points at the resource that was assigned, not at the seeded one.
        Assert.Equal(await ResourceIdAsync(AssignedResourceName), await AllocationResourceIdAsync(newSlug));
    }

    [Fact]
    public async Task Should_InsertTheNewAllocationRow_When_AssigningWithoutSpecifications()
    {
        // The parent row on its own. With no specifications there are no Added children to mask or to blame,
        // so this isolates the AssignedLocation INSERT itself: it must happen even though its parent Location
        // is tracked and its own key is already set.
        await SeedAsync();

        var result = await AssignAsync(specifications: []);

        var created = Assert.IsType<Created<AssignResourceEndpoint.Response>>(result);
        var newSlug = created.Value!.Slug;

        var counts = await CountRowsAsync();
        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(2, counts.AssignedLocations);
        Assert.Equal(new[] { SeededSlug, newSlug }.Order(), await AllocationSlugsAsync());

        // The seeded allocation keeps its own two values; the new one adds none.
        Assert.Equal(2, await AllocationValueCountAsync());
        Assert.Equal(0, await AllocationValueCountAsync(newSlug));
    }

    // ── Characterization: PUT and DELETE do NOT share T-055-08's shape ───────────────────────────────
    //
    // Both endpoints mutate an EXISTING AssignedLocation, so its key-is-set/Modified verdict is the CORRECT
    // answer there rather than the defect. The tests below prove that against a real database, together with
    // the replace-all path of the update (issue #343).

    [Fact]
    public async Task Should_UpdateDetailsInPlace_When_TheAssignmentHasNoSpecificationsToReplace()
    {
        // The update path, isolated from the replace-all case below: an allocation with no existing
        // specifications. The row is a genuine UPDATE (no new child under a tracked parent, so the key-is-set
        // branch is right) and the one supplied specification attaches as Added because its key is a SHADOW
        // property EF mints at attach time. Green today, green after the T-055-08 fix — this endpoint must not
        // be changed by it.
        await SeedAsync(seededSpecifications: []);

        var result = await UpdateAsync([new("only", "one")]);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

        var counts = await CountRowsAsync();
        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(1, counts.AssignedLocations);
        Assert.Equal([SeededSlug], await AllocationSlugsAsync());
        Assert.Equal(555, await AllocationUnitCountAsync(SeededSlug));
        Assert.Equal(1, await AllocationValueCountAsync(SeededSlug));
    }

    [Theory]
    [InlineData("seed-a=10;seed-b=20")]
    [InlineData("seed-a=1")]
    [InlineData("seed-a=1;seed-b=2;seed-c=3")]
    [InlineData("seed-a=1;seed-a=2")]
    [InlineData("only=one")]
    public async Task Should_PersistExactlyTheSuppliedSet_When_UpdateReplacesANonEmptySpecificationSet(string supplied)
    {
        // Issue #343. The allocation is LOADED with its values (FindWithAllocatedResourcesAsync includes
        // _assignedLocationValues), so ReplaceSpecifications severs a tracked required relationship. With
        // DeleteBehavior.NoAction on the "AssignedLocationId" FK, DetectChanges threw; ClientCascade lets EF
        // delete the orphans. The cases cover changed values, a dropped name, an added name, duplicate names
        // (legacy parity) and a wholly new set.
        await SeedAsync();
        var expected = supplied.Split(';').Select(pair => pair.Split('=')).Select(kv => (kv[0], kv[1])).Order().ToArray();

        var result = await UpdateAsync([.. expected.Select(s => new UpdateAssignmentEndpoint.SpecificationItem(s.Item1, s.Item2))]);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(555, await AllocationUnitCountAsync(SeededSlug));
        Assert.Equal(expected, await AllocationValuesAsync(SeededSlug));
        Assert.Equal(expected.Length, await AllocationValueCountAsync());
    }

    [Fact]
    public async Task Should_SoftDeleteInPlace_When_RemovingThroughTheEndpoint()
    {
        // RemoveAssignmentEndpoint flips IsDeleted on an existing row — a pure UPDATE, no new child under a
        // tracked parent, so the defect shape does not apply. Green today and after the fix.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var result = await RemoveAssignmentEndpoint.HandleAsync(
            ProviderNameValue, LocationNameValue, SeededSlug,
            AdminCaller(),
            scope.ServiceProvider.GetRequiredService<IProviderRepository>(),
            Options.Create(new WellKnownRoles()),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            new RecordingAuditLogger<RemoveAssignmentEndpoint.AuditCategory>(),
            NullLogger<RemoveAssignmentEndpoint.AuditCategory>.Instance,
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);

        var counts = await CountRowsAsync();
        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(1, counts.AssignedLocations);
        Assert.True(await AllocationIsDeletedAsync(SeededSlug));
    }

    /// <summary>
    /// Invokes the real <see cref="AssignResourceEndpoint.HandleAsync"/> in one DI scope with the real
    /// DatabaseContext-backed repositories and unit of work. Only <see cref="IResourceAssignmentQuery"/> is a
    /// double: it is a read port answering two cross-aggregate questions and takes no part in the write path
    /// under test (its production implementation composes IDataQuery&lt;T&gt; instances that would need a seeded
    /// AllocationUnitType lookup table this fixture deliberately does not have).
    /// </summary>
    private async Task<IResult> AssignAsync(IReadOnlyList<AssignResourceEndpoint.SpecificationItem> specifications)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();

        return await AssignResourceEndpoint.HandleAsync(
            ProviderNameValue,
            LocationNameValue,
            FromWire<AssignResourceEndpoint.Request>(new
            {
                ResourceName = AssignedResourceName,
                AllocationUnitType = nameof(EAllocationUnitType.NodeHours),
                AllocationUnitCount = 100,
                Description = "assigned by the endpoint",
                AggregationName = "agg",
                Specifications = specifications,
            }),
            AdminCaller(),
            scope.ServiceProvider.GetRequiredService<IProviderRepository>(),
            scope.ServiceProvider.GetRequiredService<IRepository<ProjectResource, ProjectResourceKey>>(),
            AssignmentQuery().Object,
            Options.Create(new WellKnownRoles()),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            new RecordingAuditLogger<AssignResourceEndpoint.AuditCategory>(),
            NullLogger<AssignResourceEndpoint.AuditCategory>.Instance,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Invokes the real <see cref="UpdateAssignmentEndpoint.HandleAsync"/> against the seeded allocation, with
    /// the same real repositories and unit of work as <see cref="AssignAsync"/>.
    /// </summary>
    private async Task<IResult> UpdateAsync(IReadOnlyList<UpdateAssignmentEndpoint.SpecificationItem> specifications)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();

        return await UpdateAssignmentEndpoint.HandleAsync(
            ProviderNameValue, LocationNameValue, SeededSlug,
            FromWire<UpdateAssignmentEndpoint.Request>(new
            {
                AllocationUnitType = nameof(EAllocationUnitType.NodeHours),
                AllocationUnitCount = 555,
                Description = "updated",
                AggregationName = "agg",
                Specifications = specifications,
            }),
            AdminCaller(),
            scope.ServiceProvider.GetRequiredService<IProviderRepository>(),
            AssignmentQuery().Object,
            Options.Create(new WellKnownRoles()),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            new RecordingAuditLogger<UpdateAssignmentEndpoint.AuditCategory>(),
            NullLogger<UpdateAssignmentEndpoint.AuditCategory>.Instance,
            TestContext.Current.CancellationToken);
    }

    private static T FromWire<T>(object body) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(body), WireJson)!;

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Mock<IResourceAssignmentQuery> AssignmentQuery()
    {
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        query.Setup(q => q.HasNonSharedLocationAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return query;
    }

    /// <summary>
    /// One provider, one SHARED location, two resources, and one already-persisted allocation (of
    /// <see cref="SeededResourceName"/>, carrying two specifications). The pre-existing allocation is what makes
    /// "exactly one NEW row, the old one untouched" a meaningful assertion.
    /// </summary>
    private async Task SeedAsync(IReadOnlyCollection<(string Name, string Value)>? seededSpecifications = null)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(ProviderNameValue), countryId: 1);
        Assert.True(provider.AddLocation(LocationName.From(LocationNameValue), "https://acme.example", ELocationType.HPC, isShared: true).IsSuccess);

        var seededResource = ProjectResource.Create(ResourceName.Create(SeededResourceName), provider, UserId.From(Guid.NewGuid()), Start, End);
        var assignedResource = ProjectResource.Create(ResourceName.Create(AssignedResourceName), provider, UserId.From(Guid.NewGuid()), Start, End);

        var assignment = provider.AssignResource(
            LocationName.From(LocationNameValue), seededResource, NodeHours, allocationUnitCount: 10,
            description: "seeded allocation", aggregationName: null, specifications: seededSpecifications ?? SeededSpecs,
            resourceHasNonSharedLocation: false, slug: SeededSlug);
        Assert.True(assignment.IsSuccess);

        context.Add(provider);
        context.Add(seededResource);
        context.Add(assignedResource);

        // Plain DbContext.SaveChanges for the seed: the real IUnitOfWork would route the durable
        // ProjectResourceActivatedEvent through the SQL-Server-bound OutboxDbContext.
        await context.SaveChangesAsync(ct);
    }

    private static async Task<ProjectResource> LoadResourceAsync(AsyncServiceScope scope, string name)
    {
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<ProjectResource, ProjectResourceKey>>();
        var resource = await repository.FindAsync(
            new ProjectResourceKey(ProviderName.From(ProviderNameValue), ResourceName.Create(name)),
            TestContext.Current.CancellationToken);
        Assert.NotNull(resource);
        return resource;
    }

    private async Task<(int Providers, int Locations, int Resources, int AssignedLocations)> CountRowsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return (
            await context.Set<ProviderAggregate>().IgnoreQueryFilters().CountAsync(ct),
            await context.Set<Location>().IgnoreQueryFilters().CountAsync(ct),
            await context.Set<ProjectResource>().IgnoreQueryFilters().CountAsync(ct),
            await context.Set<AssignedLocation>().IgnoreQueryFilters().CountAsync(ct));
    }

    private async Task<string[]> AllocationSlugsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return await context.Set<AssignedLocation>()
            .IgnoreQueryFilters()
            .Select(a => a.Slug!)
            .OrderBy(slug => slug)
            .ToArrayAsync(ct);
    }

    private async Task<int> AllocationValueCountAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        return await context.Set<AssignedLocationValue>().IgnoreQueryFilters().CountAsync(ct);
    }

    /// <summary>Value rows belonging to the allocation with the given slug, over the shadow FK.</summary>
    private async Task<int> AllocationValueCountAsync(string slug)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var allocationId = await context.Set<AssignedLocation>()
            .IgnoreQueryFilters()
            .Where(a => a.Slug == slug)
            .Select(a => a.Id)
            .SingleAsync(ct);

        return await context.Set<AssignedLocationValue>()
            .IgnoreQueryFilters()
            .CountAsync(v => EF.Property<Guid>(v, "AssignedLocationId") == allocationId, ct);
    }

    private async Task<(string Name, string Value)[]> AllocationValuesAsync(string slug)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var allocationId = await context.Set<AssignedLocation>()
            .IgnoreQueryFilters()
            .Where(a => a.Slug == slug)
            .Select(a => a.Id)
            .SingleAsync(ct);

        var values = await context.Set<AssignedLocationValue>()
            .IgnoreQueryFilters()
            .Where(v => EF.Property<Guid>(v, "AssignedLocationId") == allocationId)
            .Select(v => new { v.Name, v.Value })
            .ToListAsync(ct);

        return [.. values.Select(v => (v.Name, v.Value)).Order()];
    }

    private async Task<ProjectResourceId> AllocationResourceIdAsync(string slug)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return await context.Set<AssignedLocation>()
            .IgnoreQueryFilters()
            .Where(a => a.Slug == slug)
            .Select(a => EF.Property<ProjectResourceId>(a, "ProjectResourceId"))
            .SingleAsync(ct);
    }

    private async Task<ProjectResourceId> ResourceIdAsync(string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return await context.Set<ProjectResource>()
            .IgnoreQueryFilters()
            .Where(r => r.ResourceName == ResourceName.Create(name))
            .Select(r => EF.Property<ProjectResourceId>(r, "Id"))
            .SingleAsync(ct);
    }

    private async Task<long?> AllocationUnitCountAsync(string slug)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return await context.Set<AssignedLocation>()
            .IgnoreQueryFilters()
            .Where(a => a.Slug == slug)
            .Select(a => a.AllocationUnitCount)
            .SingleAsync(ct);
    }

    private async Task<bool> AllocationIsDeletedAsync(string slug)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return await context.Set<AssignedLocation>()
            .IgnoreQueryFilters()
            .Where(a => a.Slug == slug)
            .Select(a => a.IsDeleted)
            .SingleAsync(ct);
    }
}
