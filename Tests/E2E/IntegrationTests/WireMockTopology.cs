using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace IntegrationTests;

/// <summary>
/// In-process WireMock host standing in for the three external HTTP dependencies the child API/Gateway
/// processes reach at runtime: HEAppE (redirected via seed, ADR-0051), HashiCorp Vault's KV-v2 API
/// (the only reachable seam for HEAppE credentials — see research/vault-seam.md), and the EmailSender.
/// One server serves all three so a single base URL covers every mock.
/// </summary>
public sealed class WireMockTopology : IDisposable
{
    // Stable identity for the EmailSender mapping so the swap helper updates it in place.
    private static readonly Guid EmailMappingGuid = new("e3a1c0de-0000-0000-0000-000000000001");

    private readonly WireMockServer _server;

    private WireMockTopology(WireMockServer server) => _server = server;

    /// <summary>Base URL the API/Gateway processes point their HEAppE/Vault/Email clients at.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1056:URI-like properties should not be strings",
        Justification = "Consumed verbatim as child-process env-var values (VaultCredentials__Address, PARITY_HEAPPE_MOCK_URL, ...), which are strings.")]
    public string Url => _server.Url ?? throw new InvalidOperationException("WireMock server has no URL.");

    /// <summary>
    /// Starts the server and registers all three mapping sets: HEAppE from the AppHost's
    /// <c>heappe-mappings/</c> folder, a Vault KV-v2 stub with canned HEAppE credentials, and the
    /// EmailSender endpoint defaulting to HTTP 200.
    /// </summary>
    public static WireMockTopology Start(string heappeMappingsPath)
    {
        var topology = new WireMockTopology(WireMockServer.Start());
        topology.RegisterHeappeMappings(heappeMappingsPath);
        topology.RegisterVaultKvV2Stub();
        topology.RegisterEmailSender(statusCode: 200);
        return topology;
    }

    /// <summary>
    /// Swaps the EmailSender mapping to return the given status code for the duration of a test.
    /// B05 uses this to prove a failing send does not roll back the workflow's terminal state.
    /// </summary>
    public void SetEmailSenderStatusCode(int statusCode) => RegisterEmailSender(statusCode);

    /// <summary>Resets the EmailSender mapping back to its 200 default. Call in test teardown.</summary>
    public void ResetEmailSender() => RegisterEmailSender(statusCode: 200);

    /// <summary>
    /// Snapshot of the JSON bodies posted to the EmailSender (<c>api/Email/Html</c>). The async write flows
    /// settle off the HTTP thread and mail their side effects (the PI approval action-link, state-change
    /// notifications), so a test polls this to recover the emailed action-link token and to assert a send.
    /// Reused by B04 (token capture) and B05 (email-sent assertion).
    /// </summary>
    public IReadOnlyList<string> RecordedEmailRequestBodies() =>
        _server.LogEntries
            .Where(entry => entry.RequestMessage.Path.Contains("Email/Html", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.RequestMessage.Body ?? string.Empty)
            .ToList();

    /// <summary>
    /// The paths of every request that reached the HEAppE mappings, in arrival order. The HEAppE sync rides
    /// the durable outbox, so a test polls this rather than asserting once on the write's response.
    /// </summary>
    public IReadOnlyList<string> RecordedHeappeRequestPaths() =>
        _server.LogEntries
            .Where(entry => entry.RequestMessage.Path.Contains("/heappe/", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.RequestMessage.Path)
            .ToList();

    private void RegisterHeappeMappings(string heappeMappingsPath)
    {
        if (!Directory.Exists(heappeMappingsPath))
            throw new DirectoryNotFoundException($"HEAppE mappings folder not found: {heappeMappingsPath}");

        // ReadStaticMappings loads every *.json WireMock mapping file in the folder.
        _server.ReadStaticMappings(heappeMappingsPath);
    }

    private void RegisterVaultKvV2Stub()
    {
        // KV-v2 GET: any /v1/{mount}/data/{path} returns the canned data.data envelope
        // (HashiCorpVaultClient parses doc.data.data — see HashiCorpVaultClient.GetAsync).
        _server
            .Given(Request.Create().WithPath(new WildcardMatcher("/v1/*/data/*", ignoreCase: true)).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(VaultEnvelopeJson));

        // KV-v2 POST (write-back for DDI staging): acknowledge with a minimal KV-v2 write response.
        _server
            .Given(Request.Create().WithPath(new WildcardMatcher("/v1/*/data/*", ignoreCase: true)).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"data":{"version":1}}"""));
    }

    private void RegisterEmailSender(int statusCode)
    {
        _server
            .Given(Request.Create().WithPath(new WildcardMatcher("*Email/Html*", ignoreCase: true)).UsingPost())
            .WithGuid(EmailMappingGuid)
            .RespondWith(Response.Create().WithStatusCode(statusCode));
    }

    public void Dispose() => _server.Dispose();

    // Canned KV-v2 data.data envelope holding one HEAppE credential. The inner key is the credential
    // Name that HashiCorpVaultClient.GetExternalCredentials matches against; B04 tunes it to the exact
    // heappeIdentifier the write path requests.
    private const string VaultEnvelopeJson = """
        {
          "data": {
            "data": {
              "heappe": {
                "Name": "heappe",
                "CredentialType": "ApiKey",
                "AuthorizationHeader": "Authorization",
                "HeaderPrefix": "Bearer",
                "ApiKey": "test-heappe-api-key",
                "UserName": "heappe-user",
                "Password": "heappe-pass"
              }
            }
          }
        }
        """;
}
