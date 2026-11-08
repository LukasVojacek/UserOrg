using E2E.Fixtures;

using Microsoft.Data.SqlClient;

namespace IntegrationTests.Writes;

/// <summary>
/// Issue #386: <c>PUT v2/Users/{idpUserId}/ProviderAdmin/{providerName}</c> followed by
/// <c>DELETE v2/Users/{idpUserId}/ProviderAdmin</c> through the real API, its production unit of work and SQL
/// Server. Replacing the system permission severed the required <c>User._systemPermissions</c> relationship and
/// the save threw before reaching the database, which surfaced as a 500. The round trip must leave the user's
/// single system permission in place, changed back to the User role.
/// </summary>
/// <remarks>
/// Resets to the seed baseline after each test (<see cref="DisposeAsync"/>) so the role change never leaks into
/// another spec reading the seeded role-coverage users.
/// </remarks>
[Collection(ParityTests.ParityTestCollection.Name)]
public sealed class ProviderAdminRoleWriteTests(GatewayAttachFixture fixture) : IAsyncLifetime
{
    private const string TargetIdpUserId = "0d000000-0000-0000-0000-000000000005";
    private const string SeededProviderName = "prov1";
    private static readonly Guid SeededProviderId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await fixture.ResetToSeedAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_ChangeTheSystemPermissionInPlace_When_PromotedToProviderAdminAndReset()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();
        var before = Assert.Single(await ReadSystemPermissionsAsync(ct));

        using var promoted = await admin.PutAsync(
            new Uri($"/api/v2/Users/{TargetIdpUserId}/ProviderAdmin/{SeededProviderName}", UriKind.Relative),
            content: null,
            ct);
        await AssertStatusAsync(promoted, HttpStatusCode.NoContent, "promote to ProviderAdmin", ct);

        var afterPromote = Assert.Single(await ReadSystemPermissionsAsync(ct));
        Assert.Equal(before.Id, afterPromote.Id);
        Assert.Equal(await ReadRoleIdAsync("ProviderAdmin", ct), afterPromote.SystemRoleId);
        Assert.Equal(SeededProviderId, afterPromote.AssignedProviderId);

        using var reset = await admin.DeleteAsync(
            new Uri($"/api/v2/Users/{TargetIdpUserId}/ProviderAdmin", UriKind.Relative),
            ct);
        await AssertStatusAsync(reset, HttpStatusCode.NoContent, "reset to platform user", ct);

        var after = Assert.Single(await ReadSystemPermissionsAsync(ct));
        Assert.Equal(before.Id, after.Id);
        Assert.False(after.IsDeleted);
        Assert.Equal(await ReadRoleIdAsync("User", ct), after.SystemRoleId);
        Assert.Null(after.AssignedProviderId);
    }

    private static async Task<List<(Guid Id, bool IsDeleted, int SystemRoleId, Guid? AssignedProviderId)>> ReadSystemPermissionsAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT up.[Id], up.[IsDeleted], up.[SystemRoleId], u.[AssignedProviderId]
            FROM [dbo].[UserPermission] up
            INNER JOIN [dbo].[User] u ON u.[Id] = up.[UserId]
            WHERE u.[IdentityProviderUserId] = @idpUserId
              AND up.[ProjectId] IS NULL;
            """;
        command.Parameters.AddWithValue("@idpUserId", TargetIdpUserId);

        var rows = new List<(Guid, bool, int, Guid?)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add((reader.GetGuid(0), reader.GetBoolean(1), reader.GetInt32(2), await reader.IsDBNullAsync(3, ct) ? null : reader.GetGuid(3)));
        }

        return rows;
    }

    private static async Task<int> ReadRoleIdAsync(string roleName, CancellationToken ct)
    {
        await using var connection = new SqlConnection(ConnectionString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [Id] FROM [forge].[Roles] WHERE [Name] = @roleName";
        command.Parameters.AddWithValue("@roleName", roleName);
        return (int)(await command.ExecuteScalarAsync(ct));
    }

    private static async Task<string> AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, string step, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == expected,
            $"{step}: expected {(int)expected}, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        return body;
    }

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("PARITY_SQL_CONNECTION")
            ?? throw new InvalidOperationException("PARITY_SQL_CONNECTION was not set by the topology fixture.");
}