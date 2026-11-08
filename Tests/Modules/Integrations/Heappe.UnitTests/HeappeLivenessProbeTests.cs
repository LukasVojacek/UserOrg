using Heappe.Contracts;
using Heappe.Features.VersionResolver;
using Heappe.Features.Nexus;
using Heappe.HostContracts;
using Heappe.UnitTests.Features.Nexus;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

// Covers the liveness gate's probe (ADR 0073): heappe/Health success => alive; any non-success or transport
// error => not alive (no DB fallback); caller cancellation propagates.
public sealed class HeappeLivenessProbeTests
{
    private static readonly HeappeIdentifier Instance = new("heappe-mock");

    // The probe only resolves the route of heappe-mock; one registry serves every test of the class.
    private static readonly FakeHttpEndpoint Registry = NexusRegistryFake.Registry();

    // Returns the gate's contract, not the concrete resolver, so the tests assert IHeappeLivenessProbe — the
    // interface HeappeConnectionService actually depends on.
    private static IHeappeLivenessProbe CreateResolver(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));

        // IsInstanceAliveAsync never touches the DB resolver — pass one with an unconfigured dependency.
        var dbResolver = new HeappeVersionDatabaseResolver(Mock.Of<IHeappeVersionSource>());
        return new HeappeVersionRemoteResolver(dbResolver, factory.Object, NexusRegistryFake.Directory(Registry), NullLogger<HeappeVersionRemoteResolver>.Instance);
    }

    [Fact]
    public async Task IsInstanceAlive_returns_true_When_health_responds_success()
    {
        var resolver = CreateResolver(new MockHttpMessageHandler(System.Net.HttpStatusCode.OK, "{}"));

        Assert.True(await resolver.IsInstanceAliveAsync(Instance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsInstanceAlive_returns_false_When_health_responds_non_success()
    {
        var resolver = CreateResolver(new MockHttpMessageHandler(System.Net.HttpStatusCode.ServiceUnavailable, ""));

        Assert.False(await resolver.IsInstanceAliveAsync(Instance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsInstanceAlive_returns_false_When_probe_throws()
    {
        var resolver = CreateResolver(new ThrowingHttpMessageHandler(new HttpRequestException("connection refused")));

        Assert.False(await resolver.IsInstanceAliveAsync(Instance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsInstanceAlive_propagates_cancellation_When_caller_cancels()
    {
        var resolver = CreateResolver(new CancelObservingHandler());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.IsInstanceAliveAsync(Instance, cts.Token));
    }

    private sealed class ThrowingHttpMessageHandler(Exception toThrow) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw toThrow;
    }

    private sealed class CancelObservingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
