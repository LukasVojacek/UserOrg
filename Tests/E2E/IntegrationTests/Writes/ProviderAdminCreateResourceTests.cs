using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using E2E.Fixtures;

using IntegrationTests.Support;

using ParityTests;

namespace IntegrationTests.Writes;

/// <summary>
/// The provider-facing create path end to end: <c>POST /api/v2/Providers/{providerName}/Resources</c>
/// (CreateResourceV2) creates a resource under a provider and allocates it to a location through the async
/// create-and-allocate workflow (ADR-0043, ADR-0050, ADR-0057). It is the one write a ProviderAdmin makes
/// that drives the HEAppE side itself — <c>CheckHeappeStep</c> probes every named locations HEAppE instance
/// before the workflow commits anything — so it is where the tenant rule (ADR-0105) and the HEAppE leg meet.
///
/// Asserted here: the owning ProviderAdmin is authorized and the workflow runs to a terminal state, the HEAppE
/// mock is actually reached, and a ProviderAdmin for another provider is refused before any workflow starts.
/// </summary>
[Collection(ParityTestCollection.Name)]
public sealed class ProviderAdminCreateResourceTests(GatewayAttachFixture fixture, TestcontainersTopologyFixture topology)
{
    private const string SeededLocationName = "loc1";
    private const string CreatedResourceName = "provadmincreated";

    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Should_RunTheWorkflowAndReachHeappe_When_ProviderAdminCreatesAResourceOnItsOwnProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateProviderAdminGatewayClient();
        var heappeCallsBefore = topology.WireMock.RecordedHeappeRequestPaths().Count;

        try
        {
            using var created = await client.PostAsJsonAsync(
                new Uri($"/api/v2/Providers/{GatewayAttachFixture.OwnedProviderName}/Resources", UriKind.Relative),
                NewResourceRequest(topology.WireMock.Url),
                ct);
            var createdBody = await created.Content.ReadAsStringAsync(ct);

            Assert.True(
                created.StatusCode == HttpStatusCode.Accepted,
                $"create as the owning ProviderAdmin: expected 202, got {(int)created.StatusCode}.{Environment.NewLine}{createdBody}");

            var requestId = ReadRequestId(created, createdBody);
            var terminal = await PollUntilTerminal.ForAsync(
                async token =>
                {
                    using var status = await client.GetAsync(new Uri($"/api/v2/ResourceRequests/{requestId}", UriKind.Relative), token);
                    return await status.Content.ReadAsStringAsync(token);
                },
                body => ReadStatus(body) is "Completed" or "Failed",
                TerminalTimeout,
                PollInterval,
                $"resource request {requestId} to reach a terminal state",
                ct);

            Assert.True(
                ReadStatus(terminal) == "Completed",
                $"workflow did not complete for request {requestId}.{Environment.NewLine}{terminal}");

            var heappePaths = topology.WireMock.RecordedHeappeRequestPaths();
            Assert.True(
                heappePaths.Count > heappeCallsBefore,
                $"the workflow reached no HEAppE endpoint; recorded so far: {string.Join(", ", heappePaths)}");
        }
        finally
        {
            using var cleanup = await client.DeleteAsync(
                new Uri($"/api/v2/Providers/{GatewayAttachFixture.OwnedProviderName}/Resources/{CreatedResourceName}", UriKind.Relative),
                ct);
        }
    }

    [Fact]
    public async Task Should_Deny_When_ProviderAdminCreatesAResourceOnAnotherProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateProviderAdminGatewayClient();

        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{GatewayAttachFixture.ForeignProviderName}/Resources", UriKind.Relative),
            NewResourceRequest(topology.WireMock.Url),
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden,
            $"create on a foreign provider: expected 403, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        Assert.Contains("provider.write_forbidden", body, StringComparison.Ordinal);
    }

    // The HEAppE instance a location talks to is caller-supplied per assignment under the HEAPPE_URL
    // specification key; the CheckHeappe step probes that, falling back to the location's
    // DefaultServiceIntegrationUrl, which the seed leaves null.
    private static object NewResourceRequest(string heappeUrl) => new
    {
        Name = CreatedResourceName,
        Description = "created by ProviderAdminCreateResourceTests",
        PrimaryInvestigatorEmail = "pi@provadmin.example.test",
        StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        EndDate = new DateTimeOffset(2029, 12, 31, 0, 0, 0, TimeSpan.Zero),
        AccountString = "provadmin-acct",
        UnitType = 2,
        MappingOneToOne = true,
        UseAccountingStringForScheduler = true,
        Clusters = new[] { new { ClusterName = SeededLocationName, ScratchPath = "/scratch", ProjectPath = "/project" } },
        Usernames = new[] { "provadmin-user" },
        LicenseUrl = (string?)null,
        AssignedLocations = new[]
        {
            new
            {
                LocationName = SeededLocationName,
                AllocationUnitType = 2,
                AggregationName = (string?)null,
                AllocationUnitCount = 100L,
                AllocationDescription = "alloc",
                PreferredAuthenticationType = "SshCertificate",
                Specifications = new[]
                {
                    new { Name = "HEAPPE_URL", Value = heappeUrl },
                    new { Name = "HEAPPE_IDENTIFIER", Value = "heappe" },
                    new { Name = "cpu", Value = "16" }
                }
            }
        }
    };

    // The 202 is an AcceptedAtRoute: its Location header points at the status poll route and is the
    // contract-stable place to read the flow id from, the body shape being the endpoint's own record.
    private static Guid ReadRequestId(HttpResponseMessage response, string body)
    {
        var fromLocation = response.Headers.Location?.ToString().Split('/')[^1];
        if (Guid.TryParse(fromLocation, out var located))
            return located;

        var root = JsonDocument.Parse(body).RootElement;
        foreach (var property in root.EnumerateObject())
        {
            if (Guid.TryParse(property.Value.ToString(), out var parsed))
                return parsed;
        }

        throw new InvalidOperationException($"No request id in the 202 response.{Environment.NewLine}Location: {response.Headers.Location}{Environment.NewLine}{body}");
    }

    // The status projection serializes PascalCase, unlike the camelCase most v2 responses use, so the
    // property is matched case-insensitively rather than pinned to one spelling.
    private static string ReadStatus(string body)
    {
        foreach (var property in JsonDocument.Parse(body).RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "status", StringComparison.OrdinalIgnoreCase))
                return property.Value.ToString();
        }

        return string.Empty;
    }
}