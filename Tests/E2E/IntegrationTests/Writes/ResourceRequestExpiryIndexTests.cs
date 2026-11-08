using Microsoft.Data.SqlClient;

namespace IntegrationTests.Writes;

/// <summary>
/// Regression guard for the single-active filtered unique index (ADR-0066) for the terminal states the expiry
/// sweeper writes — <c>Expired</c> (DEC-7) and <c>Failed</c> (the engine-dead settle path):
/// <c>IX_ResourceRequests_ProjectShortName_ProviderName_ResourceName</c>'s filter is an
/// allow-list of the two non-terminal state literals (<c>PendingPiApproval</c>, <c>AwaitingResourceCreation</c>),
/// not a block-list of terminal ones — so neither row was ever enumerated by it and neither needs a migration
/// to be excluded. This runs the inserts directly against the schema-isolated
/// <c>resourceRequests.ResourceRequests</c> table (Testcontainers SQL Server) rather than through the flow/sweeper,
/// isolating the index behaviour itself from the 30-day sweep timing.
/// </summary>
/// <remarks>
/// <paramref name="fixture"/> is otherwise unused by this test's body — the constructor dependency is what makes
/// xunit initialize the Testcontainers topology (and export <c>PARITY_SQL_CONNECTION</c>) before this test runs.
/// </remarks>
public sealed class ResourceRequestExpiryIndexTests(TestcontainersTopologyFixture fixture)
{
    private const string ProjectShortName = "IDXTEST";
    private const string ProviderName = "prov1";
    private const string ResourceName = "idxgpu";
    private const string ActiveSlug = "idxtest-idxgpu-active";
    private const string ConflictSlug = "idxtest-idxgpu-conflict";

    [Theory]
    [InlineData("Expired", "idxtest-idxgpu-expired")]
    [InlineData("Failed", "idxtest-idxgpu-failed")]
    public async Task Should_ExcludeTerminalRowFromSingleActiveSlot_When_AnotherRequestForSameKeyIsInserted(
        string terminalState, string terminalSlug)
    {
        _ = fixture; // ensures the assembly-fixture topology (and PARITY_SQL_CONNECTION) is up before this runs.
        var ct = TestContext.Current.CancellationToken;
        var connectionString = Environment.GetEnvironmentVariable("PARITY_SQL_CONNECTION")
            ?? throw new InvalidOperationException("PARITY_SQL_CONNECTION is not set — the topology fixture must run first.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        try
        {
            await InsertRequestAsync(connection, terminalSlug, terminalState, ct);

            // If the filtered index still enumerated the terminal state (e.g. as a leftover block-list
            // assumption), this second insert for the identical (project, provider, resource) key would violate
            // the unique constraint and throw — it must succeed, proving the terminal row holds no slot.
            await InsertRequestAsync(connection, ActiveSlug, "PendingPiApproval", ct);

            // A third, still-active row for the same key must now be rejected — the index still enforces
            // single-active among the two non-terminal states.
            await Assert.ThrowsAsync<SqlException>(
                () => InsertRequestAsync(connection, ConflictSlug, "AwaitingResourceCreation", ct));
        }
        finally
        {
            await DeleteRequestAsync(connection, terminalSlug, ct);
            await DeleteRequestAsync(connection, ActiveSlug, ct);
            await DeleteRequestAsync(connection, ConflictSlug, ct);
        }
    }

    private static async Task InsertRequestAsync(SqlConnection connection, string slug, string state, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [resourceRequests].[ResourceRequests]
                ([Slug], [ProjectShortName], [ProviderName], [ResourceName], [RequesterIdpId], [ContactEmail],
                 [State], [TokenHash], [TokenExpiresAt], [WorkflowRequestId], [Reason], [CreatedAt], [UpdatedAt])
            VALUES
                (@slug, @project, @provider, @resource, 'idx-tester', 'pi@example.org',
                 @state, NULL, NULL, @workflowId, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """;
        command.Parameters.AddWithValue("@slug", slug);
        command.Parameters.AddWithValue("@project", ProjectShortName);
        command.Parameters.AddWithValue("@provider", ProviderName);
        command.Parameters.AddWithValue("@resource", ResourceName);
        command.Parameters.AddWithValue("@state", state);
        command.Parameters.AddWithValue("@workflowId", Guid.NewGuid());

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeleteRequestAsync(SqlConnection connection, string slug, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM [resourceRequests].[ResourceRequests] WHERE [Slug] = @slug";
        command.Parameters.AddWithValue("@slug", slug);
        await command.ExecuteNonQueryAsync(ct);
    }
}
