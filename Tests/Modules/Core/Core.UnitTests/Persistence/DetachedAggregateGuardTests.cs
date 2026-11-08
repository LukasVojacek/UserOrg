using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.Domain.OnboardingRequest;
using Core.Domain.Shared;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;
using ProjectTagAggregate = Core.Domain.Project.ProjectTag;
using ProviderAggregate = Core.Domain.Provider.Provider;
using RequestAggregate = Core.Domain.Request.Request;
using RequestId = Core.Domain.Request.RequestId;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests.Persistence;

/// <summary>
/// The detached-aggregate guard on <c>DatabaseContext</c>'s <see cref="IUnitOfWork.SaveChangesAsync"/> path
/// (T-055-03). A graph carried across scopes (a <see cref="ProviderAggregate"/> read in one scope and reused
/// in the next) is <b>detached</b> for the second context: adding anything that navigates to it cascade-marks
/// it <c>Added</c>, its shadow key is regenerated, and EF silently INSERTs a duplicate root. The distinguishing
/// signature is not the key (keys are client-generated UUID v7 by design, ADR-0061) but the fact that no
/// repository <c>AddAsync</c> was ever called for that root — so <c>SaveChanges</c> must fail loudly instead.
///
/// The file is deliberately split in two:
/// <list type="bullet">
///   <item><b>RED</b> — the guard itself; these fail until the guard lands.</item>
///   <item><b>GREEN (blast radius)</b> — every legitimate write path that must NOT trip the guard. There are
///   nine <c>IRepository&lt;T,K&gt;.AddAsync</c> overloads on <c>DatabaseContext</c>; each one has to register
///   its root, so each one gets a test here. These pass today and must still pass after the guard lands —
///   a missed overload turns creation of that aggregate red.</item>
/// </list>
///
/// <para><b>Why domain events are cleared before some saves:</b> a durable event
/// (<c>ProjectResourceActivatedEvent</c>, <c>ResourceAssignmentChangedEvent</c>) routes
/// <c>IUnitOfWork.SaveChangesAsync</c> through the transactional-outbox branch, which needs a real
/// <c>DbConnection</c> and a SQL Server outbox context — unavailable on the EF in-memory provider (see
/// <c>OutboxTransactionalIntegrationTests</c>, which is gated on a real database for exactly that reason).
/// Clearing the events keeps these tests on the plain save branch; the guard runs ahead of both branches, so
/// nothing under test is skipped.</para>
/// </summary>
public sealed class DetachedAggregateGuardTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly (string Name, string Value)[] NoSpecs = [];

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 5000;

    public DetachedAggregateGuardTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));

        _serviceProvider = services.BuildServiceProvider();
    }

    // ── RED: the guard ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Should_Throw_When_DetachedProviderIsCascadeInsertedByANewResource()
    {
        // The T-055 shape: a provider loaded in one scope, carried into the next as a plain object, and used
        // as the owner of a brand-new resource. ProjectResources.AddAsync traverses the resource's _provider
        // navigation and marks the untracked provider Added — a second provider row nobody asked for.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("guard-detached", ct);
        var detached = await LoadDetachedProviderAsync("guard-detached", ct);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var resource = ProjectResource.Create(ResourceName.Create("guard-detached-res"), detached, UserId.From(Guid.NewGuid()), Start, End);
        resource.ClearDomainEvents();
        await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

        // Premise of the test: the detached provider really is queued for INSERT.
        Assert.Equal(EntityState.Added, context.Entry(detached).State);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ((IUnitOfWork)context).SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Should_NameTheAggregateTypeAndTheRemedy_When_GuardRejectsADetachedRoot()
    {
        // A bare "it failed" is useless at 3am: the message has to say WHICH aggregate type was about to be
        // inserted behind the repositories' back, and what to do instead (load it through its repository in
        // the current scope).
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("guard-message", ct);
        var detached = await LoadDetachedProviderAsync("guard-message", ct);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var resource = ProjectResource.Create(ResourceName.Create("guard-message-res"), detached, UserId.From(Guid.NewGuid()), Start, End);
        resource.ClearDomainEvents();
        await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ((IUnitOfWork)context).SaveChangesAsync(ct));

        Assert.Contains(nameof(Core.Domain.Provider.Provider), exception.Message, StringComparison.Ordinal);
        Assert.Contains("repository", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_Throw_When_ARootAddedStraightToTheContextIsSavedThroughTheUnitOfWork()
    {
        // No navigation, no cascade — the plain signature: an AggregateRoot in Added that no repository
        // AddAsync was called for. Nothing about the key distinguishes it (client-generated UUID v7,
        // ADR-0061), only the missing repository registration.
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From("guard-bypass"), countryId: 1);
        context.Add(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ((IUnitOfWork)context).SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Should_Throw_When_TheSameRootIsReAddedAfterASuccessfulUnitOfWorkSave()
    {
        // The registration set is per-save, not per-context: it is emptied once the save completes, so a
        // repository AddAsync in an earlier save must not license a bypass in a later one.
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From("guard-reset"), countryId: 1);
        await ((IRepository<ProviderAggregate, ProviderName>)context).AddAsync(provider, ct);
        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        context.Entry(provider).State = EntityState.Detached;
        context.Add(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ((IUnitOfWork)context).SaveChangesAsync(ct));
    }

    // ── GREEN: legitimate write paths the guard must never touch ────────────────────────────────────

    [Fact]
    public async Task Should_Save_When_TheProviderIsReloadedThroughItsRepositoryInTheSameScope()
    {
        // The fix the guard's message points at: reload the aggregate in the scope that writes. The provider
        // is then tracked Unchanged, the resource is the only insert, and exactly one provider row exists.
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("guard-reloaded", ct);

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var provider = await ((IRepository<ProviderAggregate, ProviderName>)context)
                .FindAsync(ProviderName.From("guard-reloaded"), ct);
            Assert.NotNull(provider);

            var resource = ProjectResource.Create(ResourceName.Create("guard-reloaded-res"), provider!, UserId.From(Guid.NewGuid()), Start, End);
            resource.ClearDomainEvents();
            await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

            await ((IUnitOfWork)context).SaveChangesAsync(ct);
        }

        await using var verifyScope = _serviceProvider.CreateAsyncScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var providerRows = await verifyContext.Set<ProviderAggregate>()
            .CountAsync(p => p.Name == ProviderName.From("guard-reloaded"), ct);
        var resourceRows = await verifyContext.Set<ProjectResource>()
            .CountAsync(r => r.ResourceName == ResourceName.Create("guard-reloaded-res"), ct);

        Assert.Equal(1, providerRows);
        Assert.Equal(1, resourceRows);
    }

    [Fact]
    public async Task Should_Save_When_ANewAssignedLocationPointsAtAResourceAddedThroughItsRepository()
    {
        // Acceptance 4: a legitimate navigation from a child entity to a root that WAS added through its
        // repository (AssignedLocation._resource → the new ProjectResource). Both roots go in through their
        // own repositories, so both are registered and the guard has nothing to say about either.
        //
        // The provider here is created in the same unit of work rather than reloaded. That is NOT a
        // convenience: creating an AssignedLocation on a provider that is already tracked Unchanged lands
        // the new assignment in EntityState.Modified — AssignedLocation.Create pre-sets its Guid PK, and EF
        // attaches a newly discovered graph node whose key is set as Modified, which then fails the save
        // with "Attempted to update or delete an entity that does not exist in the store". That is a
        // separate defect on the same T-055 fault line (see the block handoff), unrelated to this guard and
        // not fixable by it; keeping it out of this test keeps the test about the guard.
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From("guard-assign"), countryId: 1);
        Assert.True(provider.AddLocation(LocationName.From("guard-loc"), "https://guard.test", ELocationType.HPC, isShared: true).IsSuccess);
        await ((IRepository<ProviderAggregate, ProviderName>)context).AddAsync(provider, ct);

        var resource = ProjectResource.Create(ResourceName.Create("guard-assign-res"), provider, UserId.From(Guid.NewGuid()), Start, End);
        await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

        var assignment = provider.AssignResource(
            LocationName.From("guard-loc"), resource, allocationUnitTypeId: 1, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: NoSpecs,
            resourceHasNonSharedLocation: false, slug: "guard-assign-slug");
        Assert.True(assignment.IsSuccess);
        Assert.NotNull(assignment.Value);

        provider.ClearDomainEvents();
        resource.ClearDomainEvents();
        FillShadowServiceNames(context);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(resource).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(assignment.Value!).State);
    }

    [Fact]
    public async Task Should_Save_When_MarkAssignedLocationAsAddedCascadesOntoAResourceNotYetRegistered()
    {
        // The exact call ordering InitializeProjectResourceHandler and AssignResourceEndpoint use:
        // MarkAssignedLocationAsAdded runs BEFORE the resource repository's AddAsync. Add walks the reachable
        // graph, so it cascades Added onto the new ProjectResource through AssignedLocation._resource while
        // that root is still unregistered with the guard. Nothing observes the tracker until SaveChanges, by
        // which time AddAsync has registered it — but that is an ordering property, not an obvious one, and
        // reordering the two calls would make the guard throw on the create path in production.
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From("guard-order"), countryId: 1);
        Assert.True(provider.AddLocation(LocationName.From("guard-order-loc"), "https://guard.test", ELocationType.HPC, isShared: true).IsSuccess);
        await ((IRepository<ProviderAggregate, ProviderName>)context).AddAsync(provider, ct);

        var resource = ProjectResource.Create(ResourceName.Create("guard-order-res"), provider, UserId.From(Guid.NewGuid()), Start, End);

        var assignment = provider.AssignResource(
            LocationName.From("guard-order-loc"), resource, allocationUnitTypeId: 1, allocationUnitCount: null,
            description: null, aggregationName: null, specifications: NoSpecs,
            resourceHasNonSharedLocation: false, slug: "guard-order-slug");
        Assert.True(assignment.IsSuccess);
        Assert.NotNull(assignment.Value);

        ((IProviderRepository)context).MarkAssignedLocationAsAdded(assignment.Value!);

        Assert.Equal(EntityState.Added, context.Entry(resource).State);

        await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

        provider.ClearDomainEvents();
        resource.ClearDomainEvents();
        FillShadowServiceNames(context);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(resource).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(assignment.Value!).State);
    }

    [Fact]
    public async Task Should_NotApplyTheGuard_When_SavingThroughThePlainDbContextPath()
    {
        // Acceptance 3: the guard lives on the IUnitOfWork path only. Tests (and the migration/seed tooling)
        // that Add straight onto the context and call DbContext.SaveChangesAsync keep working unchanged —
        // GuidKeyStampingTests is the canonical example.
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From("guard-plain-path"), countryId: 1);
        context.Add(provider);

        var saved = await context.SaveChangesAsync(ct);

        Assert.True(saved > 0);
    }

    // ── GREEN: one test per IRepository<T,K>.AddAsync overload (9 of them) ───────────────────────────

    [Fact]
    public async Task Should_Save_When_UserIsCreatedThroughTheUserNameKeyedRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var user = NewUser("guard-user-byname");
        await ((IRepository<UserAggregate, UserName>)context).AddAsync(user, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(user).State);
    }

    [Fact]
    public async Task Should_Save_When_UserIsCreatedThroughTheIdpIdKeyedRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var user = NewUser("guard-user-byidp");
        await ((IRepository<UserAggregate, IdpId>)context).AddAsync(user, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(user).State);
    }

    [Fact]
    public async Task Should_Save_When_ProjectIsCreatedThroughItsRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var project = NewProject("guard-project");
        await ((IRepository<ProjectAggregate, ProjectShortName>)context).AddAsync(project, ct);
        StampDomainTypeKey(context);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(project).State);
    }

    [Fact]
    public async Task Should_Save_When_RequestIsCreatedThroughTheEmailKeyedRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var request = RequestAggregate.Create(Email.Create("guard-req-byemail@example.com"), ProjectId.New(), UserId.New());
        await ((IRepository<RequestAggregate, Email>)context).AddAsync(request, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(request).State);
    }

    [Fact]
    public async Task Should_Save_When_RequestIsCreatedThroughTheRequestIdKeyedRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var request = RequestAggregate.Create(Email.Create("guard-req-byid@example.com"), ProjectId.New(), UserId.New());
        await ((IRepository<RequestAggregate, RequestId>)context).AddAsync(request, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(request).State);
    }

    [Fact]
    public async Task Should_Save_When_ProjectTagIsCreatedThroughItsRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        // The tag's AddAsync resolves its creator by UserName, so the author has to exist first — through
        // the user repository, which is itself one of the paths under test.
        var author = NewUser("guard-tag-author");
        await ((IRepository<UserAggregate, UserName>)context).AddAsync(author, ct);
        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        var tag = ProjectTagAggregate.Create(TagName.Create("guard-tag"), UserName.Create("guard-tag-author"));
        var added = await ((IRepository<ProjectTagAggregate, TagName>)context).AddAsync(tag, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.NotNull(added);
        Assert.Equal(EntityState.Unchanged, context.Entry(added!).State);
    }

    [Fact]
    public async Task Should_Save_When_OnboardingRequestIsCreatedThroughItsRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        // AddAsync resolves the project and the requester from their natural keys, so both have to be
        // committed first — again through repositories, on the same unit-of-work path.
        var project = NewProject("guard-onboarding-proj");
        await ((IRepository<ProjectAggregate, ProjectShortName>)context).AddAsync(project, ct);
        StampDomainTypeKey(context);
        var requester = NewUser("guard-onboarding-user");
        await ((IRepository<UserAggregate, IdpId>)context).AddAsync(requester, ct);
        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        var onboarding = OnboardingRequestAggregate.Create(
            new ProjectShortName("guard-onboarding-proj"),
            IdpId.Create("guard-onboarding-user"),
            Email.Create("guard-onboarding-user@example.com"),
            OnboardingRequestSlug.Create("guard-onboarding-slug"));
        await ((IRepository<OnboardingRequestAggregate, OnboardingRequestSlug>)context).AddAsync(onboarding, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(onboarding).State);
    }

    [Fact]
    public async Task Should_Save_When_ProviderIsCreatedThroughItsRepository()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From("guard-provider-create"), countryId: 1);
        await ((IRepository<ProviderAggregate, ProviderName>)context).AddAsync(provider, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(provider).State);
    }

    [Fact]
    public async Task Should_Save_When_ProjectResourceIsCreatedThroughItsRepository()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProviderAsync("guard-resource-owner", ct);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = await ((IRepository<ProviderAggregate, ProviderName>)context)
            .FindAsync(ProviderName.From("guard-resource-owner"), ct);
        Assert.NotNull(provider);

        var resource = ProjectResource.Create(ResourceName.Create("guard-resource-create"), provider!, UserId.From(Guid.NewGuid()), Start, End);
        resource.ClearDomainEvents();
        await ((IRepository<ProjectResource, ProjectResourceKey>)context).AddAsync(resource, ct);

        await ((IUnitOfWork)context).SaveChangesAsync(ct);

        Assert.Equal(EntityState.Unchanged, context.Entry(resource).State);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static UserAggregate NewUser(string idp) =>
        UserAggregate.CreateUser(Email.Create($"{idp}@example.com"), UserName.Create(idp), IdpId.Create(idp));

    private ProjectAggregate NewProject(string shortName) =>
        ProjectAggregate.Create(
            new ProjectShortName(shortName),
            ProjectName.Create(shortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);

    /// <summary>
    /// The ProjectDomainType lookup's int key is converted, and the in-memory provider does not generate one —
    /// the same workaround AuditStampingTests and GuidKeyStampingTests use. Unrelated to the guard.
    /// </summary>
    private void StampDomainTypeKey(DatabaseContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<ProjectDomainType>().Where(e => e.State == EntityState.Added))
        {
            var key = entry.Metadata.FindPrimaryKey()!.Properties[0].Name;
            entry.Property(key).CurrentValue = new ProjectDomainTypeId(_domainTypeKeySeed++);
        }
    }

    /// <summary>Commits a provider through its repository, in its own scope.</summary>
    private async Task SeedProviderAsync(string name, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(name), countryId: 1);

        await ((IRepository<ProviderAggregate, ProviderName>)context).AddAsync(provider, ct);
        FillShadowServiceNames(context);
        await ((IUnitOfWork)context).SaveChangesAsync(ct);
    }

    /// <summary>
    /// The in-memory provider does not run the SQL INSERT default for the persistence-only shadow
    /// <c>ServiceName</c> on Location/AssignedLocation; fill it the way the real INSERT would. Same
    /// workaround as <c>ProviderDetailsQueryTests.SeedProviderWithLocationAsync</c>, unrelated to the guard.
    /// </summary>
    private static void FillShadowServiceNames(DatabaseContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added
                && entry.Metadata.FindProperty("ServiceName") is not null
                && entry.Property("ServiceName").CurrentValue is null)
            {
                entry.Property("ServiceName").CurrentValue = "LEXIS";
            }
        }
    }

    /// <summary>
    /// A provider instance that no context tracks — what a cached/serialized graph looks like to the next
    /// scope's DbContext.
    /// </summary>
    private async Task<ProviderAggregate> LoadDetachedProviderAsync(string name, CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = await context.Set<ProviderAggregate>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == ProviderName.From(name), ct);

        Assert.NotNull(provider);
        return provider!;
    }
}
