using Core.DataAccess.Contexts;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Providers;

/// <summary>
/// Pins the twin resolution order of <see cref="IProviderRepository.FindIncludingDeactivatedAsync"/> through the real
/// <see cref="DatabaseContext"/> on SQLite: active row first, then <c>ModifiedAt</c> desc (NULL last), then
/// <c>CreatedAt</c> desc, then the shadow <c>Id</c>. Name twins exist only in legacy data (ADR-0027), because
/// <c>IX_Provider_Name</c> is unique among active rows only, so every twin row is seeded as raw SQL. Every test
/// except the <c>Id</c> tie-break seeds the expected winner with the higher <c>Id</c>, so a lookup ordered by
/// <c>Id</c> alone picks the other row.
/// </summary>
public sealed class ProviderTwinResolutionTests : SqliteQueryHarness
{
    private const string TwinName = "twin-co";

    private static readonly Guid LowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid HigherId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly DateTime Older = new(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Should_LoadTheActiveTwin_When_ADeactivatedTwinWasModifiedMoreRecently()
    {
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Newer, modifiedAt: Newer);
        await SeedTwinAsync(HigherId, deactivated: false, createdAt: Older, modifiedAt: Older);

        var (loadedId, provider) = await FindAsync();

        Assert.Equal(HigherId, loadedId);
        Assert.False(provider.IsDeleted);
    }

    [Fact]
    public async Task Should_LoadTheMoreRecentlyModifiedTwin_When_BothTwinsAreDeactivated()
    {
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Newer, modifiedAt: Older);
        await SeedTwinAsync(HigherId, deactivated: true, createdAt: Older, modifiedAt: Newer);

        var (loadedId, _) = await FindAsync();

        Assert.Equal(HigherId, loadedId);
    }

    [Fact]
    public async Task Should_RankTheTwinLast_When_ItsModifiedAtIsNull()
    {
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Newer, modifiedAt: null);
        await SeedTwinAsync(HigherId, deactivated: true, createdAt: Older, modifiedAt: Older);

        var (loadedId, _) = await FindAsync();

        Assert.Equal(HigherId, loadedId);
    }

    [Fact]
    public async Task Should_LoadTheNewerCreatedAtTwin_When_ModifiedAtTies()
    {
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Older, modifiedAt: Newer);
        await SeedTwinAsync(HigherId, deactivated: true, createdAt: Newer, modifiedAt: Newer);

        var (loadedId, _) = await FindAsync();

        Assert.Equal(HigherId, loadedId);
    }

    [Fact]
    public async Task Should_LoadTheNewerCreatedAtTwin_When_BothModifiedAtAreNull()
    {
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Older, modifiedAt: null);
        await SeedTwinAsync(HigherId, deactivated: true, createdAt: Newer, modifiedAt: null);

        var (loadedId, _) = await FindAsync();

        Assert.Equal(HigherId, loadedId);
    }

    [Fact]
    public async Task Should_LoadTheLowerIdTwin_When_ModifiedAtAndCreatedAtTie()
    {
        await SeedTwinAsync(HigherId, deactivated: true, createdAt: Older, modifiedAt: Newer);
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Older, modifiedAt: Newer);

        var (loadedId, _) = await FindAsync();

        Assert.Equal(LowerId, loadedId);
    }

    [Fact]
    public async Task Should_FailNotDeactivatedAndSaveCleanly_When_ReactivatingANameHeldByAnActiveTwin()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Newer, modifiedAt: Newer);
        await SeedTwinAsync(HigherId, deactivated: false, createdAt: Older, modifiedAt: Older);

        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var provider = await ((IProviderRepository)context).FindIncludingDeactivatedAsync(ProviderName.From(TwinName), ct);
        Assert.NotNull(provider);

        var result = provider.Reactivate();

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderErrorCodes.NotDeactivated, result.Error.Code);
        await context.SaveChangesAsync(ct);
        Assert.Equal(1, await ActiveTwinCountAsync());
    }

    [Fact]
    public async Task Should_DeactivateTheActiveTwin_When_DeactivatingANameWithAnActiveAndADeactivatedTwin()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedTwinAsync(LowerId, deactivated: true, createdAt: Newer, modifiedAt: Newer);
        await SeedTwinAsync(HigherId, deactivated: false, createdAt: Older, modifiedAt: Older);

        await using (var scope = ServiceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var provider = await ((IProviderRepository)context).FindIncludingDeactivatedAsync(ProviderName.From(TwinName), ct);
            Assert.NotNull(provider);

            var result = provider.Deactivate();

            Assert.True(result.IsSuccess);
            await context.SaveChangesAsync(ct);
        }

        Assert.Equal(0, await ActiveTwinCountAsync());
    }

    private async Task<(Guid LoadedId, Provider Provider)> FindAsync()
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var provider = await ((IProviderRepository)context).FindIncludingDeactivatedAsync(ProviderName.From(TwinName), TestContext.Current.CancellationToken);
        Assert.NotNull(provider);
        return (context.Entry(provider).Property<ProviderId>("Id").CurrentValue.Value, provider);
    }

    private async Task<int> ActiveTwinCountAsync()
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        return await context.Set<Provider>().CountAsync(p => p.Name == ProviderName.From(TwinName), TestContext.Current.CancellationToken);
    }

    private async Task SeedTwinAsync(Guid id, bool deactivated, DateTime createdAt, DateTime? modifiedAt)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Provider" ("Id", "Name", "ServiceName", "CountryId", "IsDeleted", "CreatedAt", "ModifiedAt")
            VALUES (@id, @name, 'LEXIS', 1, @deactivated, @createdAt, @modifiedAt);
            """,
            [
                new SqliteParameter("@id", id.ToString().ToUpperInvariant()),
                new SqliteParameter("@name", TwinName),
                new SqliteParameter("@deactivated", deactivated),
                new SqliteParameter("@createdAt", createdAt),
                new SqliteParameter("@modifiedAt", (object?)modifiedAt ?? DBNull.Value),
            ],
            TestContext.Current.CancellationToken);
    }
}
