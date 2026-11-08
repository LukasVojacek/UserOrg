using E2E.Fixtures;

using Microsoft.Data.SqlClient;

namespace IntegrationTests.Writes;

/// <summary>
/// Regression guard for the shared SQL command in <see cref="GatewayAttachFixture"/>'s user-access
/// bootstrap (B02): the project-scoped and baseline <c>UserPermission</c> inserts must each get their
/// own primary key, even when both fire within the same command execution.
/// </summary>
[Collection(ParityTests.ParityTestCollection.Name)]
public sealed class UserPermissionSeedTests(GatewayAttachFixture fixture) : IAsyncLifetime
{
    private const string SeedProjectId = "11111111-1111-1111-1111-111111111111";
    private const string ProjectOwnerUserId = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await fixture.ResetToSeedAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_AssignDistinctIds_When_BothUserPermissionInsertsFireInOneCommand()
    {
        var ct = TestContext.Current.CancellationToken;

        // Clear the owner's project-scoped AND baseline UserPermission rows so both INSERT branches
        // of the shared bootstrap command run in the same execution — the scenario a shared PK
        // parameter would collide on.
        await DeleteOwnerUserPermissionsAsync(ct);

        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var ids = await ReadOwnerUserPermissionIdsAsync(ct);

        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.NotEqual(Guid.Empty, id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    private static async Task DeleteOwnerUserPermissionsAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM [dbo].[UserPermission]
            WHERE [UserId] = @userId
              AND ([ProjectId] = @projectId OR [ProjectId] IS NULL);
            """;
        command.Parameters.AddWithValue("@userId", Guid.Parse(ProjectOwnerUserId));
        command.Parameters.AddWithValue("@projectId", Guid.Parse(SeedProjectId));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<Guid>> ReadOwnerUserPermissionIdsAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Id] FROM [dbo].[UserPermission] WHERE [UserId] = @userId";
        command.Parameters.AddWithValue("@userId", Guid.Parse(ProjectOwnerUserId));

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("PARITY_SQL_CONNECTION")
            ?? throw new InvalidOperationException("PARITY_SQL_CONNECTION was not set by the topology fixture.");
}
