using System.Net;

using Heappe.Contracts;

using Microsoft.Extensions.Time.Testing;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests.Features.Nexus;

/// <summary>
/// The directory turns a HEAppE identifier into the Nexus proxy route by joining the registry's HEAppE instances
/// (<c>GET /api/v1/heappe</c>) with the caller's Keycloak clients (<c>GET /api/v1/clients</c>), and keeps that
/// snapshot for <c>TokenExchange:Nexus:DirectoryCacheTtl</c>; the proxy base is built from
/// <c>TokenExchange:Nexus:BaseUrl</c>. This directory exists only with the token exchange flag on at startup.
/// Exercised over a fake Nexus at the HTTP boundary.
/// </summary>
public sealed class HeappeInstanceDirectoryTests
{
    private static readonly HeappeIdentifier HeappeMock = new("heappe-mock");

    [Fact]
    public async Task ResolveAsync_routes_through_the_nexus_proxy_and_joins_the_client_case_insensitively()
    {
        using var registry = NexusRegistryFake.Registry();
        var directory = NexusRegistryFake.Directory(registry);

        var result = await directory.ResolveAsync(HeappeMock, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(HeappeMock, result.Value.Identifier);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, result.Value.ProxyBaseAddress);
        Assert.Equal("heappe-mock-client", result.Value.ClientId);
        Assert.Equal(["openid", "heappe"], result.Value.Scopes);
    }

    [Fact]
    public async Task ResolveAsync_reports_instance_not_found_for_a_disabled_instance()
    {
        using var registry = NexusRegistryFake.Registry();
        var directory = NexusRegistryFake.Directory(registry);

        var result = await directory.ResolveAsync(new HeappeIdentifier("heappe-off"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.HeappeInstanceNotFound, result.Error.Code);
    }

    [Fact]
    public async Task ResolveAsync_reports_instance_not_found_for_an_identifier_nexus_does_not_know()
    {
        using var registry = NexusRegistryFake.Registry();
        var directory = NexusRegistryFake.Directory(registry);

        var result = await directory.ResolveAsync(new HeappeIdentifier("unknown"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.HeappeInstanceNotFound, result.Error.Code);
    }

    [Fact]
    public async Task ResolveAsync_still_routes_an_instance_without_a_keycloak_client()
    {
        using var registry = NexusRegistryFake.Registry(clientsJson: "[]");
        var directory = NexusRegistryFake.Directory(registry);

        var result = await directory.ResolveAsync(HeappeMock, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, result.Value.ProxyBaseAddress);
        Assert.Null(result.Value.ClientId);
        Assert.Empty(result.Value.Scopes);
    }

    [Fact]
    public async Task ResolveAsync_reports_nexus_unavailable_when_the_registry_answers_503()
    {
        using var registry = new FakeHttpEndpoint()
            .On(HttpMethod.Get, NexusRegistryFake.HeappeInstancesPath, _ => FakeHttpEndpoint.Problem(HttpStatusCode.ServiceUnavailable, "urn:config-registry:registry-unavailable"))
            .On(HttpMethod.Get, NexusRegistryFake.ClientsPath, _ => FakeHttpEndpoint.Problem(HttpStatusCode.ServiceUnavailable, "urn:config-registry:registry-unavailable"));
        var directory = NexusRegistryFake.Directory(registry);

        var result = await directory.ResolveAsync(HeappeMock, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.NexusUnavailable, result.Error.Code);
    }

    [Fact]
    public async Task ResolveAsync_fetches_the_registry_once_per_cache_ttl()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        using var registry = NexusRegistryFake.Registry();
        var directory = NexusRegistryFake.Directory(registry, time);

        await directory.ResolveAsync(HeappeMock, ct);
        time.Advance(TimeSpan.FromSeconds(59));
        await directory.ResolveAsync(HeappeMock, ct);

        Assert.Equal(1, registry.CallsTo(NexusRegistryFake.HeappeInstancesPath));

        time.Advance(TimeSpan.FromSeconds(2));
        var afterTtl = await directory.ResolveAsync(HeappeMock, ct);

        Assert.True(afterTtl.IsSuccess);
        Assert.Equal(2, registry.CallsTo(NexusRegistryFake.HeappeInstancesPath));
    }

    [Theory]
    [InlineData("https://nexus.example:8443", "https://nexus.example:8443/api/heappe/heappe-mock/")]
    [InlineData("https://nexus.example:8443/", "https://nexus.example:8443/api/heappe/heappe-mock/")]
    [InlineData("http://ingress.test/nexus", "http://ingress.test/nexus/api/heappe/heappe-mock/")]
    [InlineData("http://ingress.test/nexus/", "http://ingress.test/nexus/api/heappe/heappe-mock/")]
    public async Task ResolveAsync_builds_the_proxy_base_from_the_configured_base_url(string baseAddress, string expected)
    {
        using var registry = NexusRegistryFake.Registry();
        var directory = NexusRegistryFake.Directory(registry, settings: NexusRegistryFake.Settings(new Uri(baseAddress)));

        var result = await directory.ResolveAsync(HeappeMock, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(new Uri(expected), result.Value.ProxyBaseAddress);
    }

    [Fact]
    public async Task ResolveAsync_keeps_the_snapshot_for_the_configured_directory_cache_ttl()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
        using var registry = NexusRegistryFake.Registry();
        var directory = NexusRegistryFake.Directory(registry, time, NexusRegistryFake.Settings(directoryCacheTtl: TimeSpan.FromSeconds(10)));

        await directory.ResolveAsync(HeappeMock, ct);
        time.Advance(TimeSpan.FromSeconds(9));
        await directory.ResolveAsync(HeappeMock, ct);

        Assert.Equal(1, registry.CallsTo(NexusRegistryFake.HeappeInstancesPath));

        time.Advance(TimeSpan.FromSeconds(2));
        await directory.ResolveAsync(HeappeMock, ct);

        Assert.Equal(2, registry.CallsTo(NexusRegistryFake.HeappeInstancesPath));
    }

    [Fact]
    public async Task InvalidateAsync_makes_the_next_resolve_fetch_the_registry_again()
    {
        var ct = TestContext.Current.CancellationToken;
        using var registry = NexusRegistryFake.Registry();
        var directory = NexusRegistryFake.Directory(registry);

        await directory.ResolveAsync(HeappeMock, ct);
        await directory.InvalidateAsync(ct);
        var result = await directory.ResolveAsync(HeappeMock, ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, registry.CallsTo(NexusRegistryFake.HeappeInstancesPath));
    }
}
