using Core.DataAccess.Contexts;
using Core.Domain.Shared;
using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Exceptions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

using ELocationType = Core.Domain.Shared.ELocationType;

namespace Core.UnitTests.Features.Resource.Workflow.Handlers;

/// <summary>
/// Persistence-level reproduction of T-055: <see cref="InitializeProjectResourceHandler"/> provisions over the
/// <see cref="Provider"/> instance that travelled in the workflow step payload from the endpoint's — by then
/// disposed — DI scope. <c>ProjectResources.AddAsync</c> then cascades <see cref="EntityState.Added"/> across
/// that whole detached graph and re-INSERTs rows that already exist.
///
/// Run on the SQLite relational provider rather than EF InMemory: the bug only becomes observable as a real
/// INSERT hitting a real unique index / primary key, and InMemory enforces neither. The harness follows
/// <c>ProjectDetailsQueryTests</c> (schema from the real model via <c>GenerateCreateScript</c>), with one
/// addition — see <see cref="SqliteQueryHarness"/>.
///
/// The seed deliberately contains an <b>existing</b> <see cref="ProjectResource"/> with an allocation: without
/// it the detached graph has no pre-existing rows to duplicate and the cascade proves nothing.
///
/// <b>Reloading the provider is necessary but not sufficient</b> — see
/// <see cref="Should_InsertTheNewAllocation_When_ProviderIsLoadedInTheHandlersOwnScope"/>. Once the provider is
/// tracked, the new <see cref="AssignedLocation"/> is discovered through the tracked <see cref="Location"/>'s
/// collection and EF marks it <see cref="EntityState.Modified"/> (its PK is client-generated), while its
/// <see cref="AssignedLocationValue"/> children are marked <see cref="EntityState.Added"/>. The save then emits
/// an UPDATE against a row that does not exist. These tests pin the required END STATE, not a particular
/// implementation, so the fix has to solve that too rather than trade one exception for another.
/// </summary>
public sealed class InitializeProjectResourceHandlerPersistenceTests : SqliteQueryHarness
{
    private const string ProviderNameValue = "acme";
    private const string LocationNameValue = "location-1";
    private const string SeededResourceName = "seeded-resource";
    private const string NewResourceName = "new-resource";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly (string Name, string Value)[] NoSpecs = [];

    protected override void AddServices(IServiceCollection services)
    {
        // The same scoped DatabaseContext behind every write port — the production registration shape
        // (Core.DataAccess/ServiceCollectionExtensions.cs), which is what makes "reload in the handler's own
        // scope" fix anything at all.
        // The real IUnitOfWork routes durable domain events into OutboxDbContext, whose options are built
        // with a hardcoded UseSqlServer over this context's connection (DatabaseContext.UnitOfWork.cs,
        // GetOutboxOptions). ProjectResource.Create always raises the durable ProjectResourceActivatedEvent,
        // so on SQLite that leg dies with 'near "SAVE": syntax error' before the assertions are reached.
        // The subject here is EF change tracking and the SQL the save emits, not outbox routing, so the
        // save goes straight to the DbContext. Outbox atomicity has its own coverage in
        // Tests/Modules/Core/Core.UnitTests/Outbox/OutboxTransactionalIntegrationTests.cs.
        // T-055-02: the hook is the only way to interleave a COMPETING commit with the handler's own save
        // without touching production code. Idle (null) for every other test in this file.
        services.AddSingleton<SaveSpy>();
        services.AddScoped<IUnitOfWork>(sp => new DbContextUnitOfWork(
            sp.GetRequiredService<DatabaseContext>(),
            sp.GetRequiredService<SaveSpy>()));
        services.AddScoped<IProviderRepository>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IRepository<Provider, ProviderName>>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IRepository<ProjectResource, ProjectResourceKey>>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddSingleton<IResourceProvisioningService, ResourceProvisioningService>();
    }

    // ── Characterization: what the seed and the scope hand-off actually look like today ───────────────

    [Fact]
    public async Task Should_SeedProviderWithOneAllocatedResource()
    {
        // Pins the fixture the reproduction rests on: exactly one provider, one location, one resource and one
        // allocation exist before the handler runs. If this drifts, the cascade below has nothing to duplicate.
        await SeedAsync();

        var counts = await CountRowsAsync();

        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(1, counts.Resources);
        Assert.Equal(1, counts.AssignedLocations);
    }

    [Fact]
    public async Task Should_KeepClrKeyButLoseShadowKeys_When_ProviderGraphOutlivesItsScope()
    {
        // The mechanism, pinned as behaviour: AssignedLocation.Id is a CLR property, so it survives the detach
        // and collides with PK_AssignedLocation on re-INSERT; Provider.Id and Location.Id are shadow properties
        // living in the change tracker, so they are gone the moment the loading scope is disposed and the value
        // generator mints fresh ones. That asymmetry is why Attach/Update cannot rescue this graph.
        await SeedAsync();

        var stale = await LoadProviderInDisposedEndpointScopeAsync();
        var location = Assert.Single(stale.Locations);
        var allocation = Assert.Single(location.AssignedLocations);

        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        Assert.Equal(EntityState.Detached, context.Entry(stale).State);

        // IsKeySet is precisely what EF's graph-attach heuristic branches on, so these three assertions are the
        // bug in one place: the two parents look brand new (→ INSERT), the child looks persisted (→ PK clash).
        Assert.False(context.Entry(stale).IsKeySet);
        Assert.False(context.Entry(location).IsKeySet);
        Assert.True(context.Entry(allocation).IsKeySet);
        Assert.NotEqual(Guid.Empty, allocation.Id);
    }

    // ── Reproduction (red until the handler persists over an aggregate loaded in its own scope) ───────

    [Fact]
    public async Task Should_InsertTheNewAllocation_When_ProviderIsLoadedInTheHandlersOwnScope()
    {
        // The reload on its own is NOT enough, and this test is what says so. With the provider loaded by the
        // very context that saves, the re-INSERT cascade is gone — but the brand-new AssignedLocation is
        // discovered by DetectChanges as a new element of a TRACKED Location collection, and EF's graph-attach
        // heuristic there (NavigationFixer → AttachGraph(Added, Modified, forceStateWhenUnknownKey: false))
        // picks Modified over Added whenever the key is already set. AssignedLocation.Create client-generates
        // Id (AssignedLocation.cs:80), so the key IS set and EF emits
        //     UPDATE "AssignedLocation" SET ... WHERE "Id" = <fresh uuid v7>
        // which matches no row → DbUpdateConcurrencyException ("expected to affect 1 row(s), but actually
        // affected 0"). Its AssignedLocationValue children come out Added (their keys are minted by EF, not by
        // the domain), so the write is torn as well as wrong. Whatever the fix does about the scope, it must
        // also land the allocation row as an INSERT.
        // Verified with ChangeTracker.DebugView.LongView on this exact shape: Provider Unchanged,
        // Location Unchanged, seeded AssignedLocation Unchanged, ProjectResource Added,
        // new AssignedLocation MODIFIED, its two AssignedLocationValue rows Added.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var providerRepository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();
        var fresh = await providerRepository.FindAsync(ProviderName.From(ProviderNameValue), TestContext.Current.CancellationToken);
        Assert.NotNull(fresh);

        var handler = ActivatorUtilities.CreateInstance<InitializeProjectResourceHandler>(scope.ServiceProvider);
        await handler.HandleAsync(CreateStep(fresh.Name), TestContext.Current.CancellationToken);

        var counts = await CountRowsAsync();
        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(2, counts.Resources);
        Assert.Equal(2, counts.AssignedLocations);

        // The seeded allocation is still there and the new one was INSERTed alongside it.
        var slugs = await AllocationSlugsAsync();
        Assert.Equal(2, slugs.Length);
        Assert.Contains("seeded-allocation-slug", slugs);

        // The new allocation's two specifications land with it — the parent row and its children must not
        // disagree about whether the allocation is new.
        Assert.Equal(2, await AllocationValueCountAsync());
    }

    [Fact]
    public async Task Should_ReuseTheSeededProviderGraph_When_TheWorkerRunsTheStep()
    {
        // THE reproduction. The endpoint scope used to load the aggregate and hand it to the workflow step; the
        // worker picks the message up in a brand-new scope. Provisioning over that stale graph made AddAsync
        // force-cascade Added across all of it (DbSet.Add attaches every reachable detached entity as Added
        // regardless of key), so EF re-INSERTed Provider, Location and the already-persisted AssignedLocation —
        // the Provider insert is ordered first and died on IX_Provider_Name.
        // Reloading the provider by its natural key inside the handler's scope removes the cascade; see
        // Should_InsertTheNewAllocation_When_ProviderIsLoadedInTheHandlersOwnScope for what it does NOT remove.
        // T-055-04 removed the payload's aggregate outright, so the step below carries only the natural key —
        // the assertions are unchanged: one provider row, one location row, the new resource on the SEEDED
        // provider.
        var seededProviderId = await SeedAsync();

        await RunHandlerInWorkerScopeAsync(ProviderName.From(ProviderNameValue));

        var counts = await CountRowsAsync();
        Assert.Equal(1, counts.Providers);
        Assert.Equal(1, counts.Locations);
        Assert.Equal(2, counts.Resources);
        Assert.Equal(2, counts.AssignedLocations);

        var slugs = await AllocationSlugsAsync();
        Assert.Equal(2, slugs.Length);
        Assert.Contains("seeded-allocation-slug", slugs);

        // The new allocation's two specifications land with it — the parent row and its children must not
        // disagree about whether the allocation is new.
        Assert.Equal(2, await AllocationValueCountAsync());

        // The new resource must hang off the SEEDED provider row, not off a freshly minted duplicate.
        Assert.Equal(seededProviderId, await NewResourceProviderIdAsync());
    }

    [Fact]
    public async Task Should_ThrowPermanentProviderNotFound_When_ProviderIsGoneByTheTimeTheWorkerRuns()
    {
        // The reload's other half: once the handler resolves the provider from its own scope, a provider that
        // has disappeared between submit and execution is a permanent failure, not a silent resurrection.
        // Today the handler never looks at the database and cheerfully INSERTs the payload's graph back.
        await SeedAsync();
        await DeleteProviderRowAsync();

        var exception = await Assert.ThrowsAsync<PermanentException>(
            () => RunHandlerInWorkerScopeAsync(ProviderName.From(ProviderNameValue)));

        Assert.Equal("PROVIDER_NOT_FOUND", exception.ErrorInfo.ErrorCode);
    }

    // ── T-055-02: redelivery of the same step (ADR-0100 recovery class 1) ────────────────────────────

    [Fact]
    public async Task Should_EnforceTheProviderScopedResourceNameUniqueIndex()
    {
        // Characterization, and the load-bearing premise of every red test below it: the SQLite schema
        // generated from the real model really does carry the FILTERED unique index
        // IX_ProjectResource_ProviderId_Name (ProjectResourceConfiguration.cs:111-114,
        // WHERE [IsDeleted] = 0 AND [ProviderId] IS NOT NULL). SQLite honours partial indexes and accepts
        // bracket-quoted identifiers, so the clause survives SqliteQueryHarness's two rewrites untouched.
        // If this test ever goes green-by-vacuum (no exception), the duplicate-delivery tests below stop
        // proving anything and this harness needs a different backstop.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var provider = await context.Set<Provider>().FirstAsync(TestContext.Current.CancellationToken);

        // Same provider, same name as the already-seeded resource.
        context.Add(ProjectResource.Create(ResourceName.Create(SeededResourceName), provider, UserId.From(Guid.NewGuid()), Start, End));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("UNIQUE constraint failed", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_NotCreateASecondResourceOrAllocation_When_TheSameStepIsDeliveredTwice()
    {
        // ADR-0100 recovery class 1. RequestWorker commits RecordStepCompleted through a SEPARATE
        // WorkflowDbContext (RequestWorker.cs:157-172) from the one this handler saved with; the two commits
        // are not atomic. A crash / eviction / lease loss in between redelivers the step, and the handler runs
        // a second time over a provider that is already provisioned.
        //
        // The SAME step instance is handed over twice on purpose: a redelivery replays the payload that was
        // enqueued BEFORE the first attempt, not a re-read of current state. The resource name it carries is
        // therefore identical, which is exactly the (ProviderId, ResourceName) the unique index guards.
        //
        // Today the second call provisions unconditionally (ResourceProvisioningService.cs:28 calls
        // ProjectResource.Create with no "already done?" check), reaches SaveChanges, and dies on
        // IX_ProjectResource_ProviderId_Name. In production that DbUpdateException falls into RequestWorker's
        // generic catch (:221-236) and marks the WHOLE REQUEST Failed while the rows it "failed" to write are
        // sitting in the database — a request that succeeded reported as failed.
        var step = await SeedAndBuildRedeliveredStepAsync();

        await RunHandlerInWorkerScopeAsync(step);
        var afterFirst = await CountRowsAsync();

        await RunHandlerInWorkerScopeAsync(step);
        var afterSecond = await CountRowsAsync();

        // Nothing about the database may differ between one delivery and two.
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(1, afterSecond.Providers);
        Assert.Equal(1, afterSecond.Locations);
        Assert.Equal(2, afterSecond.Resources);
        Assert.Equal(2, afterSecond.AssignedLocations);

        // Row counts alone would still pass if the redelivery had overwritten the first attempt's allocation
        // instead of adding one; the slugs are random per Provision call, so they pin identity too.
        Assert.Equal(2, (await AllocationSlugsAsync()).Length);
        Assert.Equal(2, await AllocationValueCountAsync());
    }

    [Fact]
    public async Task Should_ReturnStepResultWithNullData_When_TheSameStepIsDeliveredTwice()
    {
        // The second delivery must be a SUCCESS, not a swallowed failure. Asserting only "did not throw" would
        // be satisfied by a handler that quietly returns some error-shaped result, and by a handler that
        // catches everything; this asserts the value the engine actually consumes — StepResult, Data null,
        // byte-identical to what the first delivery returned (RequestWorker feeds result.Data straight into
        // strategy.Transition and aggregate.RecordStepCompleted, so a different shape would move the workflow
        // to a different next step on a retry than on a first run).
        var step = await SeedAndBuildRedeliveredStepAsync();

        var first = await RunHandlerInWorkerScopeAsync(step);
        var second = await RunHandlerInWorkerScopeAsync(step);

        Assert.NotNull(first);
        Assert.Null(first.Data);

        Assert.NotNull(second);
        Assert.Null(second.Data);
    }

    [Fact]
    public async Task Should_NotSaveAtAll_When_TheRedeliveredStepFindsTheResourceAlreadyProvisioned()
    {
        // Acceptance 1's other half, and the one the row counts cannot see: on a redelivery the handler must
        // recognise the already-provisioned (providerName, resourceName) and return WITHOUT reaching
        // SaveChanges. Not a style preference — an unconditional save on every redelivery is what pushes the
        // duplicate INSERT at the unique index in the first place, and it would also stamp ModifiedAt on rows
        // nothing changed.
        //
        // Counted at the IUnitOfWork seam, so it stays true whichever port the fix uses to answer "does this
        // resource exist?". The seed does not count: it persists through DbContext.SaveChangesAsync directly.
        var step = await SeedAndBuildRedeliveredStepAsync();
        var spy = ServiceProvider.GetRequiredService<SaveSpy>();

        await RunHandlerInWorkerScopeAsync(step);
        Assert.Equal(1, spy.SaveCount);

        await RunHandlerInWorkerScopeAsync(step);
        Assert.Equal(1, spy.SaveCount);
    }

    [Fact]
    public async Task Should_TranslateTheUniqueIndexViolationToSuccess_When_ADuplicateIsCommittedBeforeTheHandlersSave()
    {
        // Acceptance 2, and the reason a pre-check alone is not the whole fix. Even with a "does this resource
        // already exist?" read in front of Provision, the check and the save are two round trips: a competing
        // delivery can commit the row in between. The SaveSpy hook commits that row for real, from its own
        // scope and its own transaction, immediately before the handler's SaveChanges — so the check (however
        // the fix chooses to implement it) has already returned "no" by the time the duplicate appears.
        //
        // The DbUpdateException this produces is real EF over the real filtered unique index; nothing is
        // mocked and nothing pattern-matches a message this test invented.
        //
        // It must be caught INSIDE InitializeProjectResourceHandler and translated to idempotent success.
        // Fixing it in RequestWorker's catch (:221-236) is explicitly out of scope — WorkflowEngine.Core
        // belongs to block T-055-06 by a human decision.
        var step = await SeedAndBuildRedeliveredStepAsync();

        ServiceProvider.GetRequiredService<SaveSpy>().Action = CommitCompetingResourceAsync;

        var result = await RunHandlerInWorkerScopeAsync(step);

        Assert.NotNull(result);
        Assert.Null(result.Data);

        // The competing delivery's row is the one that survives; the handler adds no third resource.
        var counts = await CountRowsAsync();
        Assert.Equal(2, counts.Resources);
        Assert.Equal(1, await ResourceCountByNameAsync(NewResourceName));
    }

    [Fact]
    public async Task Should_PersistCreatedBy_When_TheHandlerCreatesTheResource()
    {
        // The handler creates the ProjectResource from the step payload. Verify that CreatedBy,
        // which was set by the endpoint and serialized into the step, is persisted in the database.
        await SeedAsync();

        await using var scope = ServiceProvider.CreateAsyncScope();
        var providerRepository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();
        var fresh = await providerRepository.FindAsync(ProviderName.From(ProviderNameValue), TestContext.Current.CancellationToken);
        Assert.NotNull(fresh);

        var handler = ActivatorUtilities.CreateInstance<InitializeProjectResourceHandler>(scope.ServiceProvider);
        await handler.HandleAsync(CreateStep(fresh.Name), TestContext.Current.CancellationToken);

        // Query the new resource directly from the database to verify CreatedBy was persisted.
        var resourceRepository = scope.ServiceProvider.GetRequiredService<IRepository<ProjectResource, ProjectResourceKey>>();
        var newResource = await resourceRepository.FindAsync(
            new ProjectResourceKey(ProviderName.From(ProviderNameValue), ResourceName.Create(NewResourceName)),
            TestContext.Current.CancellationToken);

        Assert.NotNull(newResource);
        // CreatedBy should be set from the step's user identity.
        Assert.NotNull(newResource.CreatedBy);
    }

    /// <summary>Seeds one provider with one location and one already-allocated resource; returns the provider's surrogate id.</summary>
    private async Task<ProviderId> SeedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = Provider.Create(ProviderName.From(ProviderNameValue), countryId: 1);
        Assert.True(provider.AddLocation(LocationName.From(LocationNameValue), "https://acme.example", ELocationType.HPC, isShared: true).IsSuccess);

        var resource = ProjectResource.Create(ResourceName.Create(SeededResourceName), provider, UserId.From(Guid.NewGuid()), Start, End);
        var assignment = provider.AssignResource(
            LocationName.From(LocationNameValue),
            resource,
            allocationUnitTypeId: (int)EAllocationUnitType.NodeHours,
            allocationUnitCount: 10,
            description: "seeded allocation",
            aggregationName: null,
            specifications: NoSpecs,
            resourceHasNonSharedLocation: false,
            slug: "seeded-allocation-slug");
        Assert.True(assignment.IsSuccess);

        context.Add(provider);
        context.Add(resource);

        // Plain DbContext.SaveChanges for the seed: IUnitOfWork.SaveChangesAsync would route the durable
        // ProjectResourceActivatedEvent through the SQL-Server-bound OutboxDbContext, which has nothing to do
        // with what is being seeded here.
        await context.SaveChangesAsync(ct);

        return (ProviderId)context.Entry(provider).Property("Id").CurrentValue!;
    }

    /// <summary>
    /// The "endpoint" scope: loads the tracked aggregate exactly as CreateResourceOnLocationEndpoint does and
    /// then disposes its scope — the returned instance is what travels in the step payload.
    /// </summary>
    private async Task<Provider> LoadProviderInDisposedEndpointScopeAsync()
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProviderRepository>();
        var provider = await repository.FindAsync(ProviderName.From(ProviderNameValue), TestContext.Current.CancellationToken);
        Assert.NotNull(provider);
        return provider;
    }

    /// <summary>The "worker" scope: a fresh DI scope, a payload carrying only the natural key, the real handler.</summary>
    private Task<StepResult> RunHandlerInWorkerScopeAsync(ProviderName payloadProviderName)
        => RunHandlerInWorkerScopeAsync(CreateStep(payloadProviderName));

    /// <summary>
    /// The "worker" scope for a step the caller already holds — which is what a REDELIVERY is: the same
    /// payload, dequeued again, executed in a brand-new scope.
    /// </summary>
    private async Task<StepResult> RunHandlerInWorkerScopeAsync(InitializeProjectResourceStep step)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();

        // ActivatorUtilities, not a hand-written new: the handler's constructor gains IProviderRepository in the
        // fix and this test must not have to change for that.
        var handler = ActivatorUtilities.CreateInstance<InitializeProjectResourceHandler>(scope.ServiceProvider);
        return await handler.HandleAsync(step, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds the fixture and builds the step a redelivery replays.
    /// <para>
    /// T-055-04: this used to hand back the DETACHED provider aggregate the endpoint scope had loaded, because
    /// that instance was what the payload carried. The payload cannot carry an aggregate any more — it carries
    /// the natural key — so there is no stale graph left to stage. What the redelivery tests below are actually
    /// about survives untouched: the SAME step instance, enqueued once, executed twice in two fresh worker
    /// scopes. The detach mechanics the old fixture illustrated are still pinned on their own by
    /// Should_KeepClrKeyButLoseShadowKeys_When_ProviderGraphOutlivesItsScope.
    /// </para>
    /// </summary>
    private async Task<InitializeProjectResourceStep> SeedAndBuildRedeliveredStepAsync()
    {
        await SeedAsync();
        return CreateStep(ProviderName.From(ProviderNameValue));
    }

    /// <summary>
    /// The competing delivery: a fresh scope, a fresh context, its own transaction, committing the very
    /// <see cref="ProjectResource"/> the handler in flight is about to insert. Saved through
    /// <c>DbContext.SaveChangesAsync</c> so the T-055-03 detached-aggregate guard (which lives on the
    /// <see cref="IUnitOfWork"/> path) plays no part in staging the race.
    /// </summary>
    private async Task CommitCompetingResourceAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = await context.Set<Provider>().FirstAsync(p => p.Name == ProviderName.From(ProviderNameValue), ct);
        context.Add(ProjectResource.Create(ResourceName.Create(NewResourceName), provider, UserId.From(Guid.NewGuid()), Start, End));
        await context.SaveChangesAsync(ct);
    }

    private async Task<int> ResourceCountByNameAsync(string resourceName)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        return await context.Set<ProjectResource>()
            .IgnoreQueryFilters()
            .CountAsync(r => r.ResourceName == ResourceName.Create(resourceName), ct);
    }

    private async Task DeleteProviderRowAsync()
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM Provider;", TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The seeded allocation and the handler's new one, by slug. A row count alone would still pass if the save
    /// somehow overwrote the seeded allocation instead of inserting a second one.
    /// </summary>
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

    private async Task<(int Providers, int Locations, int Resources, int AssignedLocations)> CountRowsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return (
            await context.Set<Provider>().IgnoreQueryFilters().CountAsync(ct),
            await context.Set<Location>().IgnoreQueryFilters().CountAsync(ct),
            await context.Set<ProjectResource>().IgnoreQueryFilters().CountAsync(ct),
            await context.Set<AssignedLocation>().IgnoreQueryFilters().CountAsync(ct));
    }

    private async Task<ProviderId?> NewResourceProviderIdAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        return await context.Set<ProjectResource>()
            .IgnoreQueryFilters()
            .Where(r => r.ResourceName == ResourceName.Create(NewResourceName))
            .Select(r => EF.Property<ProviderId?>(r, "ProviderId"))
            .SingleAsync(ct);
    }

    private static InitializeProjectResourceStep CreateStep(ProviderName providerName) => new(
        RequestId: Guid.NewGuid(),
        ProviderName: providerName,
        Data: new Request(
            Name: NewResourceName,
            Description: "desc",
            PrimaryInvestigatorEmail: "pi@example.com",
            StartDate: Start,
            EndDate: End,
            AccountString: "ACC-1",
            UnitType: EAllocationUnitType.NodeHours,
            MappingOneToOne: true,
            UseAccountingStringForScheduler: false,
            Usernames: new List<string>() { "user@example.com" },
            LicenseUrl: null,
            BypassHeappe: false,
            Clusters: [new Cluster(ClusterName: "cluster-1", ScratchPath: "/scratch", ProjectPath: "/project")],
            AssignedLocations:
            [
                new LocationAssignment(
                    LocationName: LocationNameValue,
                    AllocationUnitType: EAllocationUnitType.NodeHours,
                    AggregationName: null,
                    AllocationUnitCount: 100,
                    AllocationDescription: "alloc",
                    PreferredAuthenticationType: "pwd",
                    // Two specifications on purpose: they become AssignedLocationValue children of the new
                    // allocation, and the state EF assigns them differs from the one it assigns their parent.
                    Specifications: [new SpecificationItem("spec-a", "value-a"), new SpecificationItem("spec-b", "value-b")])
            ]),
        new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        },
        UserId.From(Guid.NewGuid()));
}

/// <summary>
/// IUnitOfWork that persists through DbContext.SaveChangesAsync, skipping the outbox leg that is bound to
/// SQL Server. See the registration comment above for why these tests cannot use the real unit of work.
/// </summary>
// The spy is optional: AssignResourceEndpointPersistenceTests reuses this double and has no race to
// stage, so it constructs the unit of work with the context alone.
internal sealed class DbContextUnitOfWork(DatabaseContext context, SaveSpy? spy = null) : IUnitOfWork
{
    public IRepository<TAggregate, TKey> GetRepository<TAggregate, TKey>()
        where TAggregate : AggregateRoot
        => ((IUnitOfWork)context).GetRepository<TAggregate, TKey>();

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        spy?.Record();
        await (spy?.RunRaceOnceAsync() ?? Task.CompletedTask);
        return await context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Counts every <see cref="IUnitOfWork.SaveChangesAsync"/> the fixture sees, and carries a one-shot hook that
/// fires immediately before the FIRST of them. It exists to stage ADR-0100 recovery class 1 honestly: a competing delivery that COMMITS
/// its own <c>ProjectResource</c> after this handler has already decided the resource does not exist and before
/// this handler's own <c>SaveChanges</c> lands. Nothing here simulates the failure — the duplicate row is real,
/// the unique index that rejects it is the one generated from the real model, and the
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/> comes out of real EF.
///
/// Deliberately NOT a mocked <c>SaveChangesAsync</c> that throws a hand-built exception: a hand-built one
/// would let the handler pass by pattern-matching on a message this test invented, which proves nothing about
/// production. It is also deliberately blind to HOW the handler pre-checks for an existing resource — the hook
/// makes the row appear after any conceivable check, so the test constrains the outcome, not the design.
///
/// The seed saves through <c>DbContext.SaveChangesAsync</c> directly, so it never trips this.
/// </summary>
internal sealed class SaveSpy
{
    private Func<Task>? _action;
    private int _saveCount;

    /// <summary>How many times a unit of work in this fixture was asked to save. The seed does not count — it
    /// persists through <c>DbContext.SaveChangesAsync</c> directly.</summary>
    public int SaveCount => Volatile.Read(ref _saveCount);

    public Func<Task>? Action
    {
        set => _action = value;
    }

    public void Record() => Interlocked.Increment(ref _saveCount);

    public async Task RunRaceOnceAsync()
    {
        var action = Interlocked.Exchange(ref _action, null);
        if (action is not null)
        {
            await action();
        }
    }
}
