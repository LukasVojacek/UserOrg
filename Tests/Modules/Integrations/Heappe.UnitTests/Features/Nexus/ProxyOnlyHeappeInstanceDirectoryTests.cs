using Lukdrasil.Result;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests.Features.Nexus;

/// <summary>
/// With the token exchange flag off at startup every HEAppE call goes to <c>{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/</c>:
/// no registry query, no service account, no instance <c>Enabled</c> check. An identifier NexusApi does not know, or a
/// disabled instance, is answered by NexusApi with 404, not by this directory.
/// </summary>
public sealed class ProxyOnlyHeappeInstanceDirectoryTests
{
    private static readonly HeappeIdentifier HeappeMock = new("heappe-mock");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResolveAsync_routes_the_identifier_to_the_proxy_of_the_configured_base_url()
    {
        var result = await NexusRegistryFake.ProxyOnly().ResolveAsync(HeappeMock, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(State.Ok, result.State);
        Assert.Equal(HeappeMock, result.Value.Identifier);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, result.Value.ProxyBaseAddress);
    }

    [Fact]
    public async Task ResolveAsync_returns_a_route_without_a_keycloak_client_or_scopes()
    {
        var result = await NexusRegistryFake.ProxyOnly().ResolveAsync(HeappeMock, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Null(result.Value.ClientId);
        Assert.Empty(result.Value.Scopes);
    }

    [Theory]
    [InlineData("heappe-off")]
    [InlineData("unknown")]
    public async Task ResolveAsync_routes_any_identifier_without_asking_whether_nexus_knows_or_enables_it(string identifier)
    {
        var result = await NexusRegistryFake.ProxyOnly().ResolveAsync(new HeappeIdentifier(identifier), Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(new Uri($"http://nexus.test/api/heappe/{identifier}/"), result.Value.ProxyBaseAddress);
    }

    [Theory]
    [InlineData("https://nexus.example:8443", "https://nexus.example:8443/api/heappe/heappe-mock/")]
    [InlineData("https://nexus.example:8443/", "https://nexus.example:8443/api/heappe/heappe-mock/")]
    [InlineData("http://ingress.test/nexus", "http://ingress.test/nexus/api/heappe/heappe-mock/")]
    [InlineData("http://ingress.test/nexus/", "http://ingress.test/nexus/api/heappe/heappe-mock/")]
    public async Task ResolveAsync_normalizes_the_trailing_slash_of_the_base_url(string baseAddress, string expected)
    {
        var result = await NexusRegistryFake.ProxyOnly(new Uri(baseAddress)).ResolveAsync(HeappeMock, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(new Uri(expected), result.Value.ProxyBaseAddress);
    }

    [Fact]
    public async Task InvalidateAsync_does_nothing_and_the_next_resolve_still_routes()
    {
        var directory = NexusRegistryFake.ProxyOnly();

        await directory.InvalidateAsync(Ct);
        var result = await directory.ResolveAsync(HeappeMock, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, result.Value.ProxyBaseAddress);
    }
}
