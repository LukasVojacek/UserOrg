using System.Net.Http.Json;
using System.Text.Json;

using E2E.Fixtures;

using IntegrationTests.Support;

using Microsoft.Data.SqlClient;

using ParityTests;

namespace IntegrationTests.Writes;

/// <summary>
/// The deferred half of criterion 8 (DEC-7): once a request is swept to <c>Expired</c>, its action link must stop
/// working — both the anonymous preview (<c>GetResourceRequestActionEndpoint</c>) and the decision endpoint
/// (<c>HandleResourceRequestActionEndpoint</c>) must return the uniform 404
/// (<c>resource_request.action_not_found</c>), indistinguishable from a token that never existed. The sweeper's
/// own timing (30-day cutoff, hourly poll) is exercised by <c>Core.UnitTests</c>; this suite only needs the
/// storage state a sweep produces, so the test writes that state itself (<c>State=Expired</c>,
/// <c>TokenHash</c>/<c>TokenExpiresAt</c> cleared, mirroring
/// <c>ResourceRequestExpirySweeper.SettleExpiredAsync</c>) against a request created through the real
/// create-and-approve path, then proves the endpoints' reaction to it. It therefore covers the action endpoints,
/// <b>not</b> the sweeper: nothing here would fail if the sweeper stopped running.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class ResourceRequestExpiryActionTests(GatewayAttachFixture fixture, TestcontainersTopologyFixture topology)
{
    private const string ProjectShortName = "TESTPRJ";
    private const string ProviderName = "prov1";
    private const string ResourceName = "expiredaction";

    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Should_ReturnUniform404_When_ActionLinkTokenBelongsToAnExpiredRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var slug = await CreateResourceRequestAsync(owner, ct);
        var token = await PollForActionTokenAsync(slug, ct);

        await ExpireRowDirectlyAsync(slug, ct);

        using var anonymous = fixture.CreateAnonymousGatewayClient();

        using var getResponse = await anonymous.GetAsync(
            new Uri($"/api/v2/Public/ResourceRequestActions/{token}", UriKind.Relative), ct);
        await AssertActionNotFoundAsync(getResponse, ct);

        using var postResponse = await anonymous.PostAsJsonAsync(
            new Uri($"/api/v2/Public/ResourceRequestActions/{token}", UriKind.Relative),
            new { Decision = "Approve" },
            ct);
        await AssertActionNotFoundAsync(postResponse, ct);
    }

    private static async Task AssertActionNotFoundAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 404 for an expired request's action token, got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        Assert.Equal("resource_request.action_not_found", document.RootElement.GetProperty("code").GetString());
    }

    // Writes the row state a sweep leaves behind (ResourceRequestExpirySweeper.SettleExpiredAsync) straight to
    // SQL, so the assertions below are about the ACTION ENDPOINTS' reaction to an expired row and prove nothing
    // about the sweeper itself: not its cutoff arithmetic or hourly poll, not the engine Expire signal it
    // normally goes through, and not the UpdatedAt bump the projection store would apply. Those are covered by
    // ResourceRequestExpirySweeperTests in Core.UnitTests; waiting out a real 30-day clock here is not an option.
    private static async Task ExpireRowDirectlyAsync(string slug, CancellationToken ct)
    {
        var connectionString = Environment.GetEnvironmentVariable("PARITY_SQL_CONNECTION")
            ?? throw new InvalidOperationException("PARITY_SQL_CONNECTION is not set — the topology fixture must run first.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE [resourceRequests].[ResourceRequests]
            SET [State] = 'Expired', [TokenHash] = NULL, [TokenExpiresAt] = NULL
            WHERE [Slug] = @slug
            """;
        command.Parameters.AddWithValue("@slug", slug);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> CreateResourceRequestAsync(HttpClient owner, CancellationToken ct)
    {
        using var response = await owner.PostAsJsonAsync(
            new Uri($"/api/v2/Projects/{ProjectShortName}/ResourceRequests", UriKind.Relative),
            new { ProviderName, ResourceName, PrimaryInvestigatorEmail = "pi@test.cz" },
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Accepted,
            $"Expected 202 creating a resource request for '{ResourceName}', got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("Slug").GetString()
            ?? throw new InvalidOperationException("Create response did not carry a Slug.");
    }

    // Same recovery as ResourceRequestWriteTests.PollForActionTokenAsync — the plaintext token is only ever
    // emailed, never persisted (the projection stores its hash).
    private async Task<string> PollForActionTokenAsync(string slug, CancellationToken ct)
    {
        var token = await PollUntilTerminal.ForAsync(
            _ => Task.FromResult(TryExtractActionToken(slug)),
            candidate => candidate is not null,
            TerminalTimeout,
            PollInterval,
            $"PI approval email for request '{slug}'",
            ct);
        return token!;
    }

    private string? TryExtractActionToken(string slug)
    {
        string? latest = null;

        foreach (var body in topology.WireMock.RecordedEmailRequestBodies())
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("TemplateName", out var template)
                || template.GetString() != "NewProjectAssignment_PM"
                || !root.TryGetProperty("TemplateArguments", out var arguments)
                || arguments.ValueKind != JsonValueKind.Array
                || arguments.GetArrayLength() < 2
                || arguments[0].GetString() != slug)
            {
                continue;
            }

            var link = arguments[1].GetString();
            if (string.IsNullOrEmpty(link))
            {
                continue;
            }

            var lastSegment = link[(link.LastIndexOf('/') + 1)..];
            latest = Uri.UnescapeDataString(lastSegment);
        }

        return latest;
    }
}
