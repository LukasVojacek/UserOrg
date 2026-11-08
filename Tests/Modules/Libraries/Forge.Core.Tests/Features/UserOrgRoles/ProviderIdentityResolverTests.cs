using System.Reflection;

using Forge.Core.Features.UserOrgRoles;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace Forge.Core.Tests.Features.UserOrgRoles;

/// <summary>
/// Covers the <c>idpUserId → assigned provider name</c> lookup the Gateway mints an interactive
/// ProviderAdmin's <c>provider_name</c> claim from (D5), against a real relational database rather than a
/// double, so the join and its misses are exercised as SQL. The caching half matters as much as the query:
/// the claim survives a revocation for as long as the entry lives, which is the window the ADR documents.
/// </summary>
public sealed class ProviderIdentityResolverTests
{
    private const string AssignedProvider = "acme";
    private const string ProviderAdminUser = "idp-provider-admin";
    private static readonly Guid _providerId = Guid.Parse("2f4b2c1e-9a0d-4b6d-8f2a-6c5c0b1e7a31");

    // Spelled out rather than read from the resolver: this is the revocation window the ADR publishes, so
    // widening it in production has to fail here instead of quietly redefining the guarantee.
    private static readonly TimeSpan _documentedCacheTtl = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Should_ResolveProviderName_When_UserHasAssignedProvider()
    {
        await using var database = await TestDatabase.CreateAsync();
        var resolver = database.CreateResolver();

        var providerName = await resolver.GetAssignedProviderNameAsync(ProviderAdminUser, TestContext.Current.CancellationToken);

        Assert.Equal(AssignedProvider, providerName);
    }

    [Fact]
    public async Task Should_ResolveNothing_When_UserHasNoAssignedProvider()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddUserAsync("idp-plain-user", assignedProviderId: null);
        var resolver = database.CreateResolver();

        var providerName = await resolver.GetAssignedProviderNameAsync("idp-plain-user", TestContext.Current.CancellationToken);

        Assert.Null(providerName);
    }

    [Fact]
    public async Task Should_ResolveNothing_When_UserIsUnknown()
    {
        await using var database = await TestDatabase.CreateAsync();
        var resolver = database.CreateResolver();

        var providerName = await resolver.GetAssignedProviderNameAsync("idp-nobody", TestContext.Current.CancellationToken);

        Assert.Null(providerName);
    }

    /// <summary>
    /// A soft-deleted user is gone as far as the platform is concerned; their leftover assignment must not
    /// keep buying provider-tier reads.
    /// </summary>
    [Fact]
    public async Task Should_ResolveNothing_When_UserIsSoftDeleted()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddUserAsync("idp-deleted-admin", _providerId, isDeleted: true);
        var resolver = database.CreateResolver();

        var providerName = await resolver.GetAssignedProviderNameAsync("idp-deleted-admin", TestContext.Current.CancellationToken);

        Assert.Null(providerName);
    }

    [Fact]
    public async Task Should_ResolveNothing_When_AssignedProviderRowIsMissing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddUserAsync("idp-dangling-admin", Guid.Parse("9f1c7c14-1f4a-4a08-9f5f-4f2a4a9b6d10"));
        var resolver = database.CreateResolver();

        var providerName = await resolver.GetAssignedProviderNameAsync("idp-dangling-admin", TestContext.Current.CancellationToken);

        Assert.Null(providerName);
    }

    /// <summary>
    /// Revocation (<c>DELETE api/v2/Users/{idpUserId}/ProviderAdmin</c>) reaches the claim only when the cache
    /// entry lapses. Asserting both sides of the boundary is what makes the documented TTL a real number: the
    /// stale answer inside it is the cost the design accepts, and the fresh one at it is the guarantee.
    /// </summary>
    [Fact]
    public async Task Should_LoseProviderClaim_WithinCacheTtl_AfterRevocation()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));
        var resolver = database.CreateResolver(clock);
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(AssignedProvider, await resolver.GetAssignedProviderNameAsync(ProviderAdminUser, ct));

        await database.RevokeProviderAdminAsync(ProviderAdminUser);

        clock.Advance(_documentedCacheTtl - TimeSpan.FromSeconds(1));
        Assert.Equal(AssignedProvider, await resolver.GetAssignedProviderNameAsync(ProviderAdminUser, ct));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await resolver.GetAssignedProviderNameAsync(ProviderAdminUser, ct));
    }

    // HybridCache's stampede dedup runs the winning caller's captured state on a detached work item that
    // keeps serving the callers still enlisted after that caller disconnects, so a context taken from the
    // request scope can be back in the pool mid-query. Owning one per invocation, and holding none between
    // them, is what rules that out.
    [Fact]
    public async Task Should_CreateAndDisposeItsOwnContext_PerCacheMiss()
    {
        await using var database = await TestDatabase.CreateAsync();
        var resolver = database.CreateResolver();
        var ct = TestContext.Current.CancellationToken;

        await resolver.GetAssignedProviderNameAsync(ProviderAdminUser, ct);

        Assert.Equal(1, database.ContextFactory.CreatedCount);
        Assert.Equal(1, database.ContextFactory.DisposedCount);

        await resolver.GetProviderNameAsync(_providerId, ct);

        Assert.Equal(2, database.ContextFactory.CreatedCount);
        Assert.Equal(2, database.ContextFactory.DisposedCount);

        await resolver.GetAssignedProviderNameAsync(ProviderAdminUser, ct);

        Assert.Equal(2, database.ContextFactory.CreatedCount);
        Assert.Equal(2, database.ContextFactory.DisposedCount);
    }

    [Fact]
    public void Should_HoldNoDbContextField_SoCacheFactoriesCannotBeGivenARequestScopedContext()
    {
        var contextFields = typeof(ProviderIdentityResolver)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => typeof(DbContext).IsAssignableFrom(field.FieldType))
            .Select(field => field.Name);

        Assert.Empty(contextFields);
    }

    /// <summary>
    /// A SQLite database carrying the two columns of the deployed schema this lookup seeks on, so the query
    /// under test runs as SQL instead of against a stubbed context.
    /// </summary>
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly UserOrgDbContext _seedContext;

        private TestDatabase(SqliteConnection connection, UserOrgDbContext seedContext, CountingDbContextFactory contextFactory)
        {
            _connection = connection;
            _seedContext = seedContext;
            ContextFactory = contextFactory;
        }

        public CountingDbContextFactory ContextFactory { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<UserOrgDbContext>().UseSqlite(connection).Options;
            var seedContext = new UserOrgDbContext(options);

            var database = new TestDatabase(connection, seedContext, new CountingDbContextFactory(options));
            await seedContext.Database.ExecuteSqlRawAsync(
                """
                create table Provider (Id text not null primary key, Name text not null);
                create table [User] (Id text not null primary key, IdentityProviderUserId text not null,
                                     AssignedProviderId text null, IsDeleted integer not null default 0);
                """);
            await seedContext.Database.ExecuteSqlRawAsync(
                "insert into Provider (Id, Name) values ({0}, {1})", _providerId.ToString(), AssignedProvider);
            await database.AddUserAsync(ProviderAdminUser, _providerId);

            return database;
        }

        // nullif: the raw-SQL parameter API has no mapping for a null value, so "unassigned" travels as an
        // empty string and becomes NULL in the column.
        public Task AddUserAsync(string idpUserId, Guid? assignedProviderId, bool isDeleted = false)
            => _seedContext.Database.ExecuteSqlRawAsync(
                "insert into [User] (Id, IdentityProviderUserId, AssignedProviderId, IsDeleted) values ({0}, {1}, nullif({2}, ''), {3})",
                Guid.NewGuid().ToString(), idpUserId, assignedProviderId?.ToString() ?? string.Empty, isDeleted ? 1 : 0);

        public Task RevokeProviderAdminAsync(string idpUserId)
            => _seedContext.Database.ExecuteSqlRawAsync(
                "update [User] set AssignedProviderId = null where IdentityProviderUserId = {0}", idpUserId);

        public IProviderIdentityResolver CreateResolver(TimeProvider? clock = null)
            => new ProviderIdentityResolver(ContextFactory, new ExpiringHybridCache(clock ?? TimeProvider.System));

        public async ValueTask DisposeAsync()
        {
            await _seedContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CountingDbContextFactory(DbContextOptions<UserOrgDbContext> options)
        : IDbContextFactory<UserOrgDbContext>
    {
        public int CreatedCount { get; private set; }

        public int DisposedCount { get; private set; }

        public UserOrgDbContext CreateDbContext()
        {
            CreatedCount++;
            return new CountedDbContext(options, this);
        }

        private sealed class CountedDbContext(DbContextOptions<UserOrgDbContext> options, CountingDbContextFactory counter)
            : UserOrgDbContext(options)
        {
            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync();
                counter.DisposedCount++;
            }
        }
    }

    /// <summary>
    /// A <see cref="HybridCache"/> that honours <see cref="HybridCacheEntryOptions.Expiration"/> against an
    /// injectable clock. The shipped implementation expires against wall-clock time, which no test can advance.
    /// </summary>
    private sealed class ExpiringHybridCache(TimeProvider clock) : HybridCache
    {
        private readonly Dictionary<string, (object Value, DateTimeOffset ExpiresAt)> _entries = [];

        public override async ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(key, out var cached) && clock.GetUtcNow() < cached.ExpiresAt)
                return (T)cached.Value;

            var created = await factory(state, cancellationToken);
            _entries[key] = (created!, clock.GetUtcNow() + (options?.Expiration ?? TimeSpan.Zero));
            return created;
        }

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _entries.Remove(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class AdvanceableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}
