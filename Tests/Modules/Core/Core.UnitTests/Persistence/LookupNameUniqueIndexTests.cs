using Core.DataAccess.Contexts;

using Microsoft.Data.Sqlite;

namespace Core.UnitTests.Persistence;

/// <summary>
/// <c>Country.Name</c> and <c>ProjectDomainType.Name</c> are the natural identifiers the v2 read contracts
/// expose (T-196), so the schema generated from the modular <see cref="DatabaseContext"/> model must reject a
/// second row with an existing name. Runs on SQLite because EF InMemory does not enforce indexes; the harness
/// follows <c>ResourceProviderNameProjectionTests</c>. Rows are inserted with raw SQL because the constraint
/// under test lives in the database, not in the domain.
/// </summary>
public sealed class LookupNameUniqueIndexTests : SqliteQueryHarness
{
    [Theory]
    [InlineData("Country")]
    [InlineData("ProjectDomainType")]
    public async Task Should_RejectSecondRow_When_NameAlreadyExists(string table)
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertAsync(table, "Shared", ct);

        var ex = await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(table, "Shared", ct));

        Assert.Contains($"UNIQUE constraint failed: {table}.Name", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Country")]
    [InlineData("ProjectDomainType")]
    public async Task Should_AcceptRows_When_NamesDiffer(string table)
    {
        var ct = TestContext.Current.CancellationToken;
        await InsertAsync(table, "First", ct);
        await InsertAsync(table, "Second", ct);

        await using var count = Connection.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        Assert.Equal(2L, await count.ExecuteScalarAsync(ct));
    }

    private async Task InsertAsync(string table, string name, CancellationToken ct)
    {
        await using var insert = Connection.CreateCommand();
        insert.CommandText = $"INSERT INTO \"{table}\" (\"Name\") VALUES ($name)";
        insert.Parameters.AddWithValue("$name", name);
        await insert.ExecuteNonQueryAsync(ct);
    }
}
