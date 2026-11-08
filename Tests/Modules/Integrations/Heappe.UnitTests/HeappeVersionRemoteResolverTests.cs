using Heappe.Features.VersionResolver;
using Heappe.HostContracts;
using Heappe.UnitTests.Features.Nexus;

using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

/// <summary>
/// The HEAppE version comes from <c>heappe/Health</c> behind the Nexus proxy; when the probe yields no version or
/// fails, the stored version answers instead, while the caller's cancellation is never turned into that fallback.
/// </summary>
public sealed class HeappeVersionRemoteResolverTests
{
    private const string HealthPath = "/api/heappe/heappe-mock/heappe/Health";
    private const string ProjectShortName = "proj-a";

    private static readonly HeappeIdentifier HeappeMock = new("heappe-mock");
    private static readonly Version StoredInstanceVersion = new(5, 0, 0);
    private static readonly Version StoredProjectVersion = new(5, 1, 0);

    private readonly RecordingVersionSource _database = new();

    [Fact]
    public async Task GetVersion_reads_the_version_from_the_proxied_health_endpoint()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, HealthPath, _ => FakeHttpEndpoint.Json("""{"Version":"v6.4.0"}"""));

        var version = await Resolver(nexus).GetVersion(HeappeMock, TestContext.Current.CancellationToken);

        Assert.Equal(new Version(6, 4, 0), version);
        Assert.Equal(1, nexus.CallsTo(HealthPath));
        Assert.Empty(_database.Requests);
    }

    [Fact]
    public async Task GetVersion_falls_back_to_the_stored_version_when_health_names_none()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, HealthPath, _ => FakeHttpEndpoint.Json("{}"));

        var version = await Resolver(nexus).GetVersion(HeappeMock, TestContext.Current.CancellationToken);

        Assert.Equal(StoredInstanceVersion, version);
        Assert.Equal(["heappe-mock"], _database.Requests);
    }

    [Fact]
    public async Task GetVersion_for_a_project_falls_back_to_the_stored_project_version_when_the_probe_fails()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, HealthPath, _ => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused")));

        var version = await Resolver(nexus).GetVersion(ProjectShortName, HeappeMock, TestContext.Current.CancellationToken);

        Assert.Equal(StoredProjectVersion, version);
        Assert.Equal(["proj-a/heappe-mock"], _database.Requests);
    }

    [Fact]
    public async Task GetVersion_lets_the_callers_cancellation_through_without_the_stored_fallback()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Get, HealthPath, _ => FakeHttpEndpoint.Json("""{"Version":"v6.4.0"}"""));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Resolver(nexus).GetVersion(HeappeMock, cancellation.Token));

        Assert.Empty(_database.Requests);
    }

    private HeappeVersionRemoteResolver Resolver(FakeHttpEndpoint nexus) =>
        new(
            new HeappeVersionDatabaseResolver(_database),
            new FakeHttpClientFactory(nexus),
            NexusRegistryFake.Directory(nexus),
            NullLogger<HeappeVersionRemoteResolver>.Instance);

    private sealed class RecordingVersionSource : IHeappeVersionSource
    {
        public List<string> Requests { get; } = [];

        public Task<Version> GetHeappeEndpointVersions(string projectShortName, HeappeIdentifier identifier, CancellationToken cancellationToken)
        {
            Requests.Add($"{projectShortName}/{identifier}");
            return Task.FromResult(StoredProjectVersion);
        }

        public Task<Version> GetHeappeEndpointVersions(HeappeIdentifier identifier, CancellationToken cancellationToken)
        {
            Requests.Add(identifier.ToString());
            return Task.FromResult(StoredInstanceVersion);
        }
    }
}
