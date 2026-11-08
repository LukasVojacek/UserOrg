using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain.OnboardingRequest;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;
using UserAggregate = Core.Domain.User.User;

namespace Core.UnitTests.Onboarding;

/// <summary>
/// Tests for <see cref="OnboardingRequestQuery"/> over the EF in-memory provider. The aggregate's
/// ProjectId/UserId are shadow FKs, so the list queries join the Project/User sets via explicit
/// <c>.Join()</c> (not nested <c>.Any()</c>, which the in-memory provider mistranslates for multi-set
/// shadow-FK queries — repo memory). The DTOs must carry the Slug.
/// </summary>
public sealed class OnboardingRequestQueryTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;
    private int _domainTypeKeySeed = 9000;

    public OnboardingRequestQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<IOnboardingRequestQuery, OnboardingRequestQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ReturnOnlyCallerRequests_AsSent_When_SentHalfOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var projA = await SeedProjectAsync("proj-a");
        var projB = await SeedProjectAsync("proj-b");
        var alice = await SeedUserAsync("idp-alice", "alice@example.com");
        var bob = await SeedUserAsync("idp-bob", "bob@example.com");
        await SeedOnboardingAsync(projA, alice, "alice@example.com", "req-ali-proj-a-aaa111", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projB, alice, "alice@example.com", "req-ali-proj-b-bbb222", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projA, bob, "bob@example.com", "req-bob-proj-a-ccc333", OnboardingRequestState.Pending);

        var page = await ListAsync(new OnboardingRequestListFilter(IdpId.Create("idp-alice"), [], null, null), ct);

        Assert.Equal(2, page.TotalItems);
        Assert.Equal(["req-ali-proj-a-aaa111", "req-ali-proj-b-bbb222"], SlugsOf(page));
        Assert.All(page.Items, i => Assert.Equal(IdpId.Create("idp-alice"), i.IdpUserId));
        Assert.All(page.Items, i => Assert.Equal(OnboardingRequestDirection.Sent, i.Direction));
    }

    [Fact]
    public async Task Should_ReturnOnlyManagedProjectRequests_AsReceived_When_ReceivedHalfOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var projA = await SeedProjectAsync("proj-a");
        var projB = await SeedProjectAsync("proj-b");
        var alice = await SeedUserAsync("idp-alice", "alice@example.com");
        var bob = await SeedUserAsync("idp-bob", "bob@example.com");
        await SeedOnboardingAsync(projA, bob, "bob@example.com", "req-bob-proj-a-aaa111", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projB, bob, "bob@example.com", "req-bob-proj-b-bbb222", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projB, alice, "alice@example.com", "req-ali-proj-b-ccc333", OnboardingRequestState.Pending);

        var page = await ListAsync(new OnboardingRequestListFilter(null, [ProjectShortName.Create("proj-a")], null, null), ct);

        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalItems);
        Assert.Equal("req-bob-proj-a-aaa111", item.Slug!.Value.Value);
        Assert.Equal(ProjectShortName.Create("proj-a"), item.ProjectShortName);
        Assert.Equal(IdpId.Create("idp-bob"), item.IdpUserId);
        Assert.Equal(OnboardingRequestState.Pending, item.State);
        Assert.Equal(OnboardingRequestDirection.Received, item.Direction);
    }

    [Fact]
    public async Task Should_ReturnBothHalves_WithDirection_When_BothRequested()
    {
        var ct = TestContext.Current.CancellationToken;
        var projA = await SeedProjectAsync("proj-a");
        var projB = await SeedProjectAsync("proj-b");
        var alice = await SeedUserAsync("idp-alice", "alice@example.com");
        var bob = await SeedUserAsync("idp-bob", "bob@example.com");
        await SeedOnboardingAsync(projB, alice, "alice@example.com", "req-ali-proj-b-bbb222", OnboardingRequestState.Pending, id: new Guid("00000002-0000-0000-0000-000000000000"));
        await SeedOnboardingAsync(projA, bob, "bob@example.com", "req-bob-proj-a-aaa111", OnboardingRequestState.Pending, id: new Guid("00000001-0000-0000-0000-000000000000"));
        await SeedOnboardingAsync(projB, bob, "bob@example.com", "req-bob-proj-b-ccc333", OnboardingRequestState.Pending);

        var page = await ListAsync(new OnboardingRequestListFilter(IdpId.Create("idp-alice"), [ProjectShortName.Create("proj-a")], null, null), ct);

        Assert.Equal(2, page.TotalItems);
        Assert.Equal(
            [("req-ali-proj-b-bbb222", OnboardingRequestDirection.Sent), ("req-bob-proj-a-aaa111", OnboardingRequestDirection.Received)],
            page.Items.Select(i => (i.Slug!.Value.Value, i.Direction)).OrderBy(x => x.Value).ToList());
        Assert.Equal(["req-bob-proj-a-aaa111", "req-ali-proj-b-bbb222"], page.Items.Select(i => i.Slug!.Value.Value));
    }

    [Fact]
    public async Task Should_ReturnOverlapOnce_AsReceived_When_CallerSentToManagedProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var projA = await SeedProjectAsync("proj-a");
        var alice = await SeedUserAsync("idp-alice", "alice@example.com");
        await SeedOnboardingAsync(projA, alice, "alice@example.com", "req-ali-proj-a-aaa111", OnboardingRequestState.Pending);

        var page = await ListAsync(new OnboardingRequestListFilter(IdpId.Create("idp-alice"), [ProjectShortName.Create("proj-a")], null, null), ct);

        var item = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalItems);
        Assert.Equal(OnboardingRequestDirection.Received, item.Direction);
    }

    [Fact]
    public async Task Should_NarrowBothHalves_When_ProjectFilterGiven()
    {
        var ct = TestContext.Current.CancellationToken;
        var projA = await SeedProjectAsync("proj-a");
        var projB = await SeedProjectAsync("proj-b");
        var projC = await SeedProjectAsync("proj-c");
        var alice = await SeedUserAsync("idp-alice", "alice@example.com");
        var bob = await SeedUserAsync("idp-bob", "bob@example.com");
        await SeedOnboardingAsync(projA, alice, "alice@example.com", "req-ali-proj-a-aaa111", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projC, alice, "alice@example.com", "req-ali-proj-c-bbb222", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projB, bob, "bob@example.com", "req-bob-proj-b-ccc333", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projA, bob, "bob@example.com", "req-bob-proj-a-ddd444", OnboardingRequestState.Pending);

        OnboardingRequestListFilter Filter(string project) => new(
            IdpId.Create("idp-alice"), [ProjectShortName.Create("proj-b")], ProjectShortName.Create(project), null);

        Assert.Equal(["req-ali-proj-a-aaa111"], SlugsOf(await ListAsync(Filter("proj-a"), ct)));
        Assert.Equal(["req-bob-proj-b-ccc333"], SlugsOf(await ListAsync(Filter("proj-b"), ct)));
        Assert.Empty((await ListAsync(Filter("proj-x"), ct)).Items);
    }

    [Fact]
    public async Task Should_FilterByState_When_StateFilterGiven()
    {
        var ct = TestContext.Current.CancellationToken;
        var projA = await SeedProjectAsync("proj-a");
        var projB = await SeedProjectAsync("proj-b");
        var projC = await SeedProjectAsync("proj-c");
        var alice = await SeedUserAsync("idp-alice", "alice@example.com");
        var bob = await SeedUserAsync("idp-bob", "bob@example.com");
        var carol = await SeedUserAsync("idp-carol", "carol@example.com");
        await SeedOnboardingAsync(projB, alice, "alice@example.com", "req-ali-proj-b-aaa111", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(projC, alice, "alice@example.com", "req-ali-proj-c-bbb222", OnboardingRequestState.Accepted);
        await SeedOnboardingAsync(projA, bob, "bob@example.com", "req-bob-proj-a-ccc333", OnboardingRequestState.Accepted);
        await SeedOnboardingAsync(projA, carol, "carol@example.com", "req-car-proj-a-ddd444", OnboardingRequestState.Rejected);

        var page = await ListAsync(
            new OnboardingRequestListFilter(IdpId.Create("idp-alice"), [ProjectShortName.Create("proj-a")], null, OnboardingRequestState.Accepted.Value), ct);

        Assert.Equal(2, page.TotalItems);
        Assert.Equal(["req-ali-proj-c-bbb222", "req-bob-proj-a-ccc333"], SlugsOf(page));
        Assert.All(page.Items, i => Assert.Equal(OnboardingRequestState.Accepted, i.State));
    }

    [Fact]
    public async Task Should_ReturnEmptyPage_When_NoHalfRequested()
    {
        var ct = TestContext.Current.CancellationToken;
        var noQueryAllowed = new Mock<IDataQuery<OnboardingRequest>>(MockBehavior.Strict);
        var query = new OnboardingRequestQuery(noQueryAllowed.Object);

        var page = await query.GetForCallerAsync(
            new OnboardingRequestListFilter(null, [], ProjectShortName.Create("proj-a"), null), page: 1, limit: 20, ct);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task Should_FindPendingId_When_PendingRequestExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var proj = await SeedProjectAsync("proj-pend");
        var userId = await SeedUserAsync("idp-p", "p@example.com");
        await SeedOnboardingAsync(proj, userId, "p@example.com", "req-p-proj-pend-aaa111", OnboardingRequestState.Pending);

        var id = await QueryAsync(q => q.FindPendingIdAsync(proj, userId, ct));
        Assert.NotNull(id);

        // No pending row for a settled request.
        var proj2 = await SeedProjectAsync("proj-settled");
        await SeedOnboardingAsync(proj2, userId, "p@example.com", "req-p-proj-settled-bbb222", OnboardingRequestState.Rejected);
        Assert.Null(await QueryAsync(q => q.FindPendingIdAsync(proj2, userId, ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_FindIdBySlug_When_SlugExists_AndNull_When_SoftDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var proj = await SeedProjectAsync("proj-slug");
        var userId = await SeedUserAsync("idp-s", "s@example.com");
        await SeedOnboardingAsync(proj, userId, "s@example.com", "req-s-proj-slug-aaa111", OnboardingRequestState.Pending);
        await SeedOnboardingAsync(proj, userId, "s@example.com", "req-s-proj-slug-deleted", OnboardingRequestState.Pending, deleted: true);

        Assert.NotNull(await QueryAsync(q => q.FindIdBySlugAsync("req-s-proj-slug-aaa111", ct)).ConfigureAwait(false));
        Assert.Null(await QueryAsync(q => q.FindIdBySlugAsync("req-s-proj-slug-deleted", ct)).ConfigureAwait(false));
        Assert.Null(await QueryAsync(q => q.FindIdBySlugAsync("req-does-not-exist", ct)).ConfigureAwait(false));
    }

    [Fact]
    public async Task Should_AnswerAsAMiss_When_SlugIsStructurallyInvalid()
    {
        // A slug the value object would reject must be indistinguishable from one that simply is not there:
        // the Accept/Reject endpoints derive their byte-identical not_found from a null query result, so any
        // other outcome here (a throw -> 500, or a distinct 400) would leak that the slug is well-formed.
        var ct = TestContext.Current.CancellationToken;
        var proj = await SeedProjectAsync("proj-inv");
        var userId = await SeedUserAsync("idp-i", "i@example.com");
        await SeedOnboardingAsync(proj, userId, "i@example.com", "req-i-proj-inv-aaa111", OnboardingRequestState.Pending);

        const string absent = "req-i-proj-inv-zzz999";
        var overLong = new string('a', OnboardingRequestSlug.MaxLength + 1);

        Assert.Equal(
            await QueryAsync(q => q.FindIdBySlugAsync(absent, ct)),
            await QueryAsync(q => q.FindIdBySlugAsync(overLong, ct)));
        Assert.Equal(
            await QueryAsync(q => q.FindSettlementBySlugAsync(absent, ct)),
            await QueryAsync(q => q.FindSettlementBySlugAsync(overLong, ct)));
    }

    private Task<QueryPage<OnboardingRequestSummary>> ListAsync(OnboardingRequestListFilter filter, CancellationToken ct) =>
        QueryAsync(q => q.GetForCallerAsync(filter, page: 1, limit: 20, ct));

    private static List<string> SlugsOf(QueryPage<OnboardingRequestSummary> page) =>
        [.. page.Items.Select(i => i.Slug!.Value.Value).Order(StringComparer.Ordinal)];

    private async Task<TResult> QueryAsync<TResult>(Func<IOnboardingRequestQuery, Task<TResult>> act)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IOnboardingRequestQuery>();
        return await act(query);
    }

    private async Task<ProjectId> SeedProjectAsync(string shortName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var projectId = ProjectId.From(Guid.NewGuid());
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

        await context.SaveChangesAsync(ct);
        return projectId;
    }

    private async Task<UserId> SeedUserAsync(string idp, string email)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var userId = UserId.From(Guid.NewGuid());
        var user = UserAggregate.CreateUser(Email.Create(email), UserName.Create(idp), IdpId.Create(idp));
        context.Add(user);
        context.Entry(user).Property("Id").CurrentValue = userId;
        await context.SaveChangesAsync(ct);
        return userId;
    }

    private async Task SeedOnboardingAsync(ProjectId projectId, UserId userId, string email, string slug, OnboardingRequestState state, bool deleted = false, Guid? id = null)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        // Build directly via the aggregate, then set the shadow members the write-adapter would set in
        // production (the test seeds the surrogate id and FK shadows itself).
        var request = OnboardingRequest.Create(
            new ProjectShortName($"seed-{Guid.NewGuid():N}"),
            IdpId.Create("seed-idp"),
            Email.Create(email),
            OnboardingRequestSlug.Create(slug));

        if (state == OnboardingRequestState.Accepted)
            Assert.True(request.Accept().IsSuccess);
        else if (state == OnboardingRequestState.Rejected)
            Assert.True(request.Reject().IsSuccess);

        if (deleted)
            Assert.True(request.Delete().IsSuccess);

        var entry = context.Add(request);
        entry.Property("Id").CurrentValue = id ?? Guid.CreateVersion7();
        entry.Property("ProjectId").CurrentValue = projectId;
        entry.Property("UserId").CurrentValue = userId;
        await context.SaveChangesAsync(ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
