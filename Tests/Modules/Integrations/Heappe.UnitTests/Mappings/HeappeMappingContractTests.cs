using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Heappe.Contracts.HEappeApiDto.Responses;

using Xunit;

namespace Heappe.UnitTests.Mappings;

/// <summary>
/// Contract test over the static mapping files in <c>src/UserOrg.AppHost/heappe-mappings/</c>: one case per row
/// of the create-resource traffic table (T-059), sent with a plain <see cref="HttpClient"/> against an
/// in-process WireMock that loaded exactly those files. The DTO each body is deserialised into is the one
/// <c>HeappeService</c> (or <c>HeappeVersionRemoteResolver</c>) uses for that call, and the asserted fields are
/// the ones the handlers read — so a green run means the create-resource flow can be driven against the mock.
/// </summary>
public sealed class HeappeMappingContractTests(HeappeMappingsFixture fixture) : IClassFixture<HeappeMappingsFixture>
{
    // Mirrors how HeappeService deserialises every HEAppE response: PascalCase on the wire, no naming policy.
    private static readonly JsonSerializerOptions HeappeJson = new() { PropertyNameCaseInsensitive = true };

    // Priority of 900-catchall-get.json, the floor kept for the background workers (ClusterInfoCacheWarmer,
    // HeappeResourceAuditWorker). Every traffic row owns a mapping strictly below it.
    private const int CatchAllPriority = 900;

    // ProjectCreationService.MinimalVersionForPreferredAuthTypeProperty — HeappeService.cs:578 rejects anything below.
    private static readonly Version MinimalHeappeVersion = new(6, 4, 0);

    // AppHost's Heappe:ClusterAuthTypes:AllowedAuthenticationTypeNames minus Kerberos: the two names a request's
    // PreferredAuthenticationType can carry and still survive the filter in GetClusterAuthenticationCredentialsAuthTypesAsync.
    private static readonly string[] RequiredAuthTypeNames = ["SshCertificate", "PrivateKey"];

    // The two hosts the mock is reached on: localhost under `aspire run` (port 9099), 127.0.0.1 from the
    // in-process WireMock the E2E topology starts. ClusterEndpointMatcher does a substring match of the login
    // node (the HEAppE URL host) against MasterNodeName, so both have to be inside the one string.
    private static readonly string[] RequiredMasterNodeHosts = ["localhost", "127.0.0.1"];

    // Row 1 — GET /heappe/Health. HeappeVersionRemoteResolver.cs:84-101 parses the "Version" string property and
    // falls back to the DB-stored 5.0.0 for anything else, which fails the >= 6.4.0 gate at HeappeService.cs:578.
    [Fact]
    public async Task Health_ReturnsAVersionObjectAtLeastSixFourZero()
    {
        var body = await GetStringAsync(HttpMethod.Get, "/heappe/Health");

        using var document = JsonDocument.Parse(body);

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.True(
            document.RootElement.TryGetProperty("Version", out var versionElement),
            $"GET /heappe/Health must answer an object with a 'Version' property; got: {body}");
        Assert.Equal(JsonValueKind.String, versionElement.ValueKind);

        var raw = versionElement.GetString()?.Trim().TrimStart('v', 'V');
        Assert.True(Version.TryParse(raw, out var parsed), $"'{raw}' is not a parsable Version.");
        Assert.True(parsed >= MinimalHeappeVersion, $"Health reported {parsed}, below the required {MinimalHeappeVersion}.");
    }

    // Row 2 — GET /heappe/Dictionary/GetClusterAuthenticationCredentialsAuthTypes. The answer is filtered against
    // AllowedAuthenticationTypeNames (HeappeService.cs:600-604); an answer that does not intersect leaves the
    // caller with an empty list and the request's PreferredAuthenticationType unmatchable.
    [Fact]
    public async Task ClusterAuthenticationCredentialsAuthTypes_CoverSshCertificateAndPrivateKey()
    {
        var authTypes = await GetJsonAsync<IList<ClusterAuthenticationCredentialAuthType>>(
            HttpMethod.Get, "/heappe/Dictionary/GetClusterAuthenticationCredentialsAuthTypes");

        Assert.NotNull(authTypes);
        var names = authTypes.Select(t => t.Name).ToList();
        foreach (var required in RequiredAuthTypeNames)
        {
            Assert.Contains(required, names, StringComparer.OrdinalIgnoreCase);
        }
    }

    // Row 3 — GET /heappe/Management/Clusters. ProjectCreationService.cs:66 picks the first cluster whose
    // MasterNodeName matches the login node name, which InitializeHeappeHandler.cs:93 fills with the HEAppE URL.
    [Fact]
    public async Task Clusters_ReturnOneClusterWhoseMasterNodeNameCoversBothMockHosts()
    {
        var clusters = await GetJsonAsync<IList<ClusterDetailedModel>>(HttpMethod.Get, "/heappe/Management/Clusters");

        Assert.NotNull(clusters);
        var cluster = Assert.Single(clusters);
        Assert.Equal(1, cluster.Id);
        Assert.False(string.IsNullOrWhiteSpace(cluster.MasterNodeName), "MasterNodeName must be set — the cluster match is a substring test against it.");
        foreach (var host in RequiredMasterNodeHosts)
        {
            Assert.Contains(host, cluster.MasterNodeName, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Row 4 — GET /heappe/Management/ProjectsByAccountingStrings?AccountingString=…. An empty list is what sends
    // EnsureProjectAsync down the create branch (GetProjectByResourceName maps Count 0 to State.NotFound).
    [Fact]
    public async Task ProjectsByAccountingStrings_AnswerAnEmptyProjectList()
    {
        var projects = await GetJsonAsync<IList<ProjectModel>>(
            HttpMethod.Get, "/heappe/Management/ProjectsByAccountingStrings?AccountingString=ACC-SIM");

        Assert.NotNull(projects);
        Assert.Empty(projects);
    }

    // Row 5 — POST /heappe/Management/Project. The GET catch-all does not answer writes, so without a mapping
    // this 404s; the Id is what every later row and the resource's HeappeProjectId are keyed on.
    [Fact]
    public async Task CreateProject_ReturnsAProjectWithIdFortyTwo()
    {
        var project = await GetJsonAsync<ProjectModel>(
            HttpMethod.Post,
            "/heappe/Management/Project",
            """{"Name":"sim","AccountingString":"ACC-SIM","Description":"","UsageType":1}""");

        Assert.NotNull(project);
        Assert.Equal(42, project.Id);
    }

    // Row 6 — GET /heappe/Management/ProjectAssignmentToClusters?ProjectId=42. Empty means "not assigned yet",
    // which is the create branch of EnsureProjectAssignmentToClusterAsync.
    [Fact]
    public async Task ProjectAssignmentToClusters_AnswerAnEmptyList()
    {
        var assignments = await GetJsonAsync<IList<ProjectAssignmentToClusterModel>>(
            HttpMethod.Get, "/heappe/Management/ProjectAssignmentToClusters?ProjectId=42");

        Assert.NotNull(assignments);
        Assert.Empty(assignments);
    }

    // Row 7 — POST /heappe/Management/ProjectAssignmentToCluster.
    [Fact]
    public async Task CreateProjectAssignmentToCluster_ReturnsTheAssignmentOfClusterOneToProjectFortyTwo()
    {
        var assignment = await GetJsonAsync<ProjectAssignmentToClusterModel>(
            HttpMethod.Post,
            "/heappe/Management/ProjectAssignmentToCluster",
            """{"ProjectId":42,"ClusterId":1,"ScratchStoragePath":"/scratch","ProjectStoragePath":"/project"}""");

        Assert.NotNull(assignment);
        Assert.Equal(1, assignment.ClusterId);
        Assert.Equal(42, assignment.ProjectId);
    }

    // Row 8 — GET /heappe/Management/ClusterNodeTypeAggregations. EnsureProjectClusterNodeTypeAggregationAsync
    // looks the request's AggregationName up in this list by Name; nothing found ends the step in a failure.
    [Fact]
    public async Task ClusterNodeTypeAggregations_ContainTheMockAggregation()
    {
        var aggregations = await GetJsonAsync<IList<ClusterNodeTypeAggregationModel>>(
            HttpMethod.Get, "/heappe/Management/ClusterNodeTypeAggregations");

        Assert.NotNull(aggregations);
        var aggregation = Assert.Single(aggregations, a => string.Equals(a.Name, "mock-aggregation", StringComparison.Ordinal));
        Assert.True(aggregation.Id > 0, "The aggregation needs a non-zero Id — it is posted back as ClusterNodeTypeAggregationId.");
    }

    // Row 9 — GET /heappe/Management/ProjectClusterNodeTypeAggregations?ProjectId=42.
    [Fact]
    public async Task ProjectClusterNodeTypeAggregations_AnswerAnEmptyList()
    {
        var aggregations = await GetJsonAsync<IList<ProjectClusterNodeTypeAggregationModel>>(
            HttpMethod.Get, "/heappe/Management/ProjectClusterNodeTypeAggregations?ProjectId=42");

        Assert.NotNull(aggregations);
        Assert.Empty(aggregations);
    }

    // Row 10 — POST /heappe/Management/ProjectClusterNodeTypeAggregation.
    [Fact]
    public async Task CreateProjectClusterNodeTypeAggregation_ReturnsTheAggregationBoundToProjectFortyTwo()
    {
        var aggregation = await GetJsonAsync<ProjectClusterNodeTypeAggregationModel>(
            HttpMethod.Post,
            "/heappe/Management/ProjectClusterNodeTypeAggregation",
            """{"ProjectId":42,"ClusterNodeTypeAggregationId":7,"AllocationAmount":1000}""");

        Assert.NotNull(aggregation);
        Assert.Equal(42, aggregation.ProjectId);
        Assert.Equal(7, aggregation.ClusterNodeTypeAggregationId);
    }

    // Row 11 — POST /heappe/Management/GenerateSecureShellKey (the method HeappeService.GenerateSecureShellKeyAsync
    // sends). InitializeHeappeHandler.cs:114 reads element [0] of the answer, so an empty list is an
    // IndexOutOfRange, and the public key is what the flow stores.
    [Fact]
    public async Task GenerateSecureShellKey_ReturnsANonEmptyKeyWithAPublicOpenSshKey()
    {
        var keys = await GetJsonAsync<IList<SecureShellKeyModel>>(
            HttpMethod.Post,
            "/heappe/Management/GenerateSecureShellKey",
            """{"ProjectId":42,"Credentials":[{"Username":"simuser"}]}""");

        Assert.NotNull(keys);
        Assert.NotEmpty(keys);
        Assert.False(
            string.IsNullOrWhiteSpace(keys[0].PublicKeyOpenSSH),
            "GenerateSecureShellKey[0].PublicKeyOpenSSH must be non-empty — the flow stores it on the account.");
    }

    // Row 12 — PUT /heappe/Management/TestClusterAccessForAccount. Any IsClusterAccessible=false fails step 4.
    [Fact]
    public async Task TestClusterAccessForAccount_ReportsEveryClusterAccessible()
    {
        var availabilities = await GetJsonAsync<IList<ClusterAvailabilityModel>>(
            HttpMethod.Put,
            "/heappe/Management/TestClusterAccessForAccount",
            """{"ProjectId":42,"Username":"simuser"}""");

        Assert.NotNull(availabilities);
        Assert.NotEmpty(availabilities);
        Assert.All(availabilities, a => Assert.True(a.IsClusterAccessible, $"Cluster '{a.ClusterName}' is reported inaccessible."));
    }

    // Nexus row 1 — POST /api/v1/auth/token. In the E2E topology the WireMock stands in for
    // NexusApi, and NexusServiceTokenProvider reads access_token and expires_in from this answer (ApiContracts.cs:12-15).
    [Fact]
    public async Task NexusToken_IssuesABearerTokenWithAnExpiry()
    {
        var body = await GetStringAsync(
            HttpMethod.Post, "/api/v1/auth/token", """{"client_id":"userorg","client_secret":"userorg-secret"}""");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("access_token").GetString()), $"access_token must be set; got: {body}");
        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.True(root.GetProperty("expires_in").GetInt32() > 0, $"expires_in must be positive; got: {body}");
    }

    // Nexus row 2 — GET /api/v1/heappe. The directory routes only enabled instances; the AppHost seeds
    // heappe-mock, the E2E create-resource flow sends the identifier heappe.
    [Fact]
    public async Task NexusHeappeInstances_ListHeappeMockAndHeappeEnabled()
    {
        var body = await GetStringAsync(HttpMethod.Get, "/api/v1/heappe");

        using var document = JsonDocument.Parse(body);
        using var items = document.RootElement.EnumerateArray();
        var instances = items
            .ToDictionary(i => i.GetProperty("name").GetString()!, i => i.GetProperty("enabled").GetBoolean(), StringComparer.Ordinal);
        Assert.True(instances.GetValueOrDefault("heappe-mock"), $"heappe-mock must be listed and enabled; got: {body}");
        Assert.True(instances.GetValueOrDefault("heappe"), $"heappe must be listed and enabled; got: {body}");
    }

    // Nexus row 3 — GET /api/v1/clients. The Keycloak clients the userorg service account may use, joined to the
    // instances by name (the route carries ClientId/Scopes for the later token exchange).
    [Fact]
    public async Task NexusClients_ListHeappeMockAndHeappe()
    {
        var body = await GetStringAsync(HttpMethod.Get, "/api/v1/clients");

        using var document = JsonDocument.Parse(body);
        using var items = document.RootElement.EnumerateArray();
        var clients = items
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("clientId").GetString(), StringComparer.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(clients.GetValueOrDefault("heappe-mock")), $"heappe-mock must be listed with a clientId; got: {body}");
        Assert.False(string.IsNullOrWhiteSpace(clients.GetValueOrDefault("heappe")), $"heappe must be listed with a clientId; got: {body}");
    }

    // Nexus row 4 — a HEAppE call addressed through the proxy prefix /api/heappe/{identifier}/ is answered by the
    // same HEAppE row as the bare path, so the stand-in serves both the registry and the proxied instance.
    [Fact]
    public async Task ProxiedHealth_IsAnsweredUnderTheNexusProxyPrefix()
    {
        var body = await GetStringAsync(HttpMethod.Get, "/api/heappe/heappe-mock/heappe/Health");

        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("Version", out _), $"the proxied Health must carry a Version; got: {body}");
    }

    // The 900-catchall-get.json floor exists for the background workers (ClusterInfoCacheWarmer,
    // HeappeResourceAuditWorker) and must stay GET-only: a write that has no mapping has to fail loudly rather
    // than be swallowed by a 200 [] that the caller would read as success.
    [Fact]
    public async Task UnmappedPost_IsNotAnsweredByTheGetCatchAll()
    {
        using var request = BuildRequest(HttpMethod.Post, "/heappe/Management/Nope", "{}");
        using var response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, string? body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<string> GetStringAsync(HttpMethod method, string path, string? body = null)
    {
        using var request = BuildRequest(method, path, body);
        using var response = await fixture.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{method} {path} was answered {(int)response.StatusCode} by the mappings in '{fixture.MappingsPath}'. Body: {content}"));

        // Status code alone cannot tell a real stub from the GET catch-all wherever the expected body is `[]`,
        // so every row also has to prove its own mapping answered — priority strictly below the catch-all's 900.
        var priority = fixture.PriorityOfMappingThatAnsweredLastRequest();
        Assert.True(
            priority < CatchAllPriority,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{method} {path} was answered by a mapping at priority {priority}: the {CatchAllPriority} catch-all swallowed it instead of a dedicated mapping file in '{fixture.MappingsPath}'."));

        return content;
    }

    private async Task<T?> GetJsonAsync<T>(HttpMethod method, string path, string? body = null)
    {
        var content = await GetStringAsync(method, path, body);
        try
        {
            return JsonSerializer.Deserialize<T>(content, HeappeJson);
        }
        catch (JsonException ex)
        {
            Assert.Fail($"{method} {path} answered a body that is not a {typeof(T).Name}: {ex.Message}. Body: {content}");
            throw;
        }
    }
}