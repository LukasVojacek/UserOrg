using System.Net;
using System.Text.Json;

using Heappe.Contracts;
using Heappe.Contracts.Entries.Init;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Features.Connection;
using Heappe.Features.Credentials;
using Heappe.Features.InitializationProjectWithClusters;
using Heappe.Features.KillSwitch;
using Heappe.Features.VersionResolver;
using Heappe.HostContracts;
using Heappe.Helpers.Connection;
using Heappe.Persistence;
using Heappe.UnitTests.Features.Nexus;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests.Features.InitializationProjectWithClusters;

/// <summary>
/// Initializing a HEAppE project for a resource runs entirely through the Nexus proxy of the instance's identifier:
/// an identifier Nexus cannot route stops the flow before any HEAppE call, and otherwise the project is ensured on
/// the cluster whose master node is the location's login node. The connection factory, the directory, the version
/// resolver and <see cref="HeappeService"/> are the real ones over a fake Nexus endpoint; Vault is the fake port.
/// </summary>
public sealed class ProjectCreationServiceTests
{
    private const string Heappe = "/api/heappe/heappe-mock/heappe";
    private const string ClustersPath = Heappe + "/Management/Clusters";
    private const string ProjectsByAccountingStringsPath = Heappe + "/Management/ProjectsByAccountingStrings";
    private const string ProjectPath = Heappe + "/Management/Project";
    private const string AssignmentsPath = Heappe + "/Management/ProjectAssignmentToClusters";
    private const string AssignmentPath = Heappe + "/Management/ProjectAssignmentToCluster";
    private const string HealthPath = Heappe + "/Health";
    private const string ClusterAggregationsPath = Heappe + "/Management/ClusterNodeTypeAggregations";
    private const string ProjectAggregationsPath = Heappe + "/Management/ProjectClusterNodeTypeAggregations";
    private const string ProjectAggregationPath = Heappe + "/Management/ProjectClusterNodeTypeAggregation";
    private const string LoginNode = "login1.karolina.example.org";

    private const string KarolinaCluster = """[{ "Id": 3, "Name": "Karolina", "MasterNodeName": "login1.karolina.example.org" }]""";

    private static readonly HeappeIdentifier HeappeMock = new("heappe-mock");

    [Fact]
    public async Task Initialize_stops_before_any_heappe_call_when_nexus_cannot_route_the_identifier()
    {
        using var nexus = NexusRegistryFake.Registry();

        var result = await Service(nexus).InitializeHEAppEProjectWithClustersAsync(
            Specification(new HeappeIdentifier("unknown")), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.HeappeInstanceNotFound, result.Error.Code);
        Assert.DoesNotContain(nexus.RequestUris, uri => uri.AbsolutePath.StartsWith("/api/heappe/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Initialize_fails_when_heappe_does_not_list_its_clusters()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, ClustersPath, _ => FakeHttpEndpoint.Json("boom", HttpStatusCode.InternalServerError));

        var result = await Service(nexus).InitializeHEAppEProjectWithClustersAsync(Specification(HeappeMock), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Failed to retrieve clusters from HEAppE instance 'heappe-mock'.", result.Error.Description);
    }

    [Fact]
    public async Task Initialize_fails_when_no_cluster_has_the_login_node_as_master()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, ClustersPath, _ => FakeHttpEndpoint.Json("""[{ "Id": 4, "Name": "Barbora", "MasterNodeName": "login1.barbora.example.org" }]"""));

        var result = await Service(nexus).InitializeHEAppEProjectWithClustersAsync(Specification(HeappeMock), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal($"HEAppE instance 'heappe-mock' does not contain a cluster with login node name '{LoginNode}'.", result.Error.Description);
        Assert.Equal(0, nexus.CallsTo(ProjectsByAccountingStringsPath));
    }

    [Fact]
    public async Task Initialize_fails_when_heappe_refuses_to_create_the_project()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, ClustersPath, _ => FakeHttpEndpoint.Json(KarolinaCluster))
            .On(HttpMethod.Get, ProjectsByAccountingStringsPath, _ => FakeHttpEndpoint.Json("[]"))
            .On(HttpMethod.Post, ProjectPath, _ => FakeHttpEndpoint.Json("boom", HttpStatusCode.InternalServerError));

        var result = await Service(nexus).InitializeHEAppEProjectWithClustersAsync(Specification(HeappeMock), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, nexus.CallsTo(AssignmentsPath));
    }

    [Fact]
    public async Task Initialize_creates_the_project_and_assigns_it_to_the_cluster_through_the_proxy()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, ClustersPath, _ => FakeHttpEndpoint.Json(KarolinaCluster))
            .On(HttpMethod.Get, ProjectsByAccountingStringsPath, _ => FakeHttpEndpoint.Json("[]"))
            .On(HttpMethod.Post, ProjectPath, _ => FakeHttpEndpoint.Json("""{ "Id": 42, "Name": "proj-a", "AccountingString": "RES-1" }"""))
            .On(HttpMethod.Get, AssignmentsPath, _ => FakeHttpEndpoint.Json("[]"))
            .On(HttpMethod.Post, AssignmentPath, _ => FakeHttpEndpoint.Json("""{ "ClusterId": 3, "ProjectId": 42 }"""))
            .On(HttpMethod.Get, HealthPath, _ => FakeHttpEndpoint.Json("""{ "Version": "v6.4.0" }"""));

        var result = await Service(nexus).InitializeHEAppEProjectWithClustersAsync(Specification(HeappeMock), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(42, result.Value);
        Assert.Equal(1, nexus.CallsTo(ProjectPath));
        Assert.Equal(1, nexus.CallsTo(AssignmentPath));
    }

    [Fact]
    public async Task Initialize_updates_an_existing_node_type_aggregation_with_the_aggregation_and_project_ids_in_their_own_fields()
    {
        UpdateProjectClusterNodeTypeAggregationRequest? sent = null;
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, ClustersPath, _ => FakeHttpEndpoint.Json(KarolinaCluster))
            .On(HttpMethod.Get, ProjectsByAccountingStringsPath, _ => FakeHttpEndpoint.Json("""
                [{ "Id": 42, "Name": "proj-a", "Description": "Project A", "AccountingString": "RES-1",
                   "StartDate": "2026-01-01T00:00:00Z", "EndDate": "2026-12-31T00:00:00Z", "UsageType": 0,
                   "UseAccountingStringForScheduler": false, "IsOneToOneMapping": false }]
                """))
            .On(HttpMethod.Get, AssignmentsPath, _ => FakeHttpEndpoint.Json("""[{ "ClusterId": 3, "ProjectId": 42, "ScratchStoragePath": "/scratch", "ProjectStoragePath": "/project" }]"""))
            .On(HttpMethod.Get, ClusterAggregationsPath, _ => FakeHttpEndpoint.Json("""[{ "Id": 7, "Name": "KAROLINA_CPU" }]"""))
            .On(HttpMethod.Get, ProjectAggregationsPath, _ => FakeHttpEndpoint.Json("""[{ "ProjectId": 42, "ClusterNodeTypeAggregationId": 7, "AllocationAmount": 100 }]"""))
            .On(HttpMethod.Put, ProjectAggregationPath, async request =>
            {
                sent = JsonSerializer.Deserialize<UpdateProjectClusterNodeTypeAggregationRequest>(
                    await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken), JsonSerializerOptions.Web);
                return FakeHttpEndpoint.Json("""{ "ProjectId": 42, "ClusterNodeTypeAggregationId": 7, "AllocationAmount": 200 }""");
            });

        var result = await Service(nexus).InitializeHEAppEProjectWithClustersAsync(
            Specification(HeappeMock, new AllocationSpecificationEntry("KAROLINA_CPU", 200)), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.NotNull(sent);
        Assert.Equal(7, sent.ClusterNodeTypeAggregationId);
        Assert.Equal(42, sent.ProjectId);
        Assert.Equal(200, sent.AllocationAmount);
    }

    private static HeappeProjectSpecificationEntry Specification(HeappeIdentifier identifier, AllocationSpecificationEntry? allocation = null) =>
        new(
            HeappeUri: null,
            identifier,
            new ProjectSpecificationEntry(
                "proj-a",
                "RES-1",
                "Project A",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                default,
                UseAccountingStringForScheduler: false,
                "pi@example.org",
                IsOneToOneMapping: false,
                PreferredAuthType: 0),
            new ClusterSpecificationEntry(LoginNode, "/project", "/scratch", allocation ?? new AllocationSpecificationEntry(NodeTypeAggregationName: null, 0)));

    private static ProjectCreationService Service(FakeHttpEndpoint nexus)
    {
        var directory = NexusRegistryFake.Directory(nexus);
        var versions = new HeappeVersionRemoteResolver(
            new HeappeVersionDatabaseResolver(Mock.Of<IHeappeVersionSource>()),
            new FakeHttpClientFactory(nexus),
            directory,
            NullLogger<HeappeVersionRemoteResolver>.Instance);
        var heappe = new HeappeService(
            NullLogger<HeappeService>.Instance,
            new FakeHttpClientFactory(nexus),
            new Mock<IFeatureManager>().Object,
            new Mock<IDbContextFactory<HeappeIntegrationDbContext>>().Object,
            new Mock<IKillSwitchStateService>().Object,
            versions,
            Options.Create(new ClusterAuthTypeOptions()));

        return new ProjectCreationService(
            NullLogger<ProjectCreationService>.Instance,
            heappe,
            versions,
            new HeappeConnectionFactory(directory, new ServiceKeyVault(), heappe, Mock.Of<IHeappeTokenExchanger>(MockBehavior.Strict)));
    }

    private sealed class ServiceKeyVault : ICredentialsService
    {
        public Task<Result<HeappeCredential, Error>> GetCredentials(string heappeIdentifier, CancellationToken cancellationToken) =>
            Task.FromResult(Result<HeappeCredential, Error>.Success(new HeappeCredential { SecretApiKey = "service-key" }, State.Ok));
    }
}
