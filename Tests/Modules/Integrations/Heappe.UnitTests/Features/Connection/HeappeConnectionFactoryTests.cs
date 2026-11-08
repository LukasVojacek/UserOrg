using System.Collections.Concurrent;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Features.Connection;
using Heappe.Features.Credentials;
using Heappe.Features.KillSwitch;
using Heappe.Features.VersionResolver;
using Heappe.Helpers.Connection;
using Heappe.Persistence;
using Heappe.UnitTests.Features.Nexus;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Moq;

using TokenDelegation.Models;

using UserOrg.Shared;

using Xunit;

using static Heappe.UnitTests.Features.Connection.TokenExchangeValues;

namespace Heappe.UnitTests.Features.Connection;

/// <summary>
/// Every HEAppE connection the factory hands out (public, service, user) addresses the instance through the
/// Nexus proxy <c>{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/</c>; the service key comes from Vault by
/// identifier. The <c>TokenExchangeEnabled</c> flag is read once at startup and decides the composition: with it off
/// the factory runs over <see cref="Heappe.Features.Nexus.ProxyOnlyHeappeInstanceDirectory"/> and
/// <see cref="PassThroughTokenExchanger"/>, so a user call sends the caller's own token and nothing reads the registry;
/// with it on the factory runs over the registry directory and <see cref="HeappeTokenExchanger"/>, so a user call
/// sends only the token exchanged for the instance's Keycloak client. Either way <c>HEAppEAuthHeader</c> and
/// <c>useToken</c> only choose how the token reaches HEAppE. The directories, the user authentication
/// (<see cref="HeappeService"/>) and the exchangers are the real ones, over fakes at the HTTP boundary and for the
/// delegation service; Vault is the fake port.
/// </summary>
public sealed class HeappeConnectionFactoryTests : IDisposable
{
    private const string AuthenticateLexisTokenPath = "/api/heappe/heappe-mock/heappe/UserAndLimitationManagement/AuthenticateLexisToken";
    private const string ListAvailableClustersPath = "/api/heappe/heappe-mock/heappe/ClusterInformation/ListAvailableClusters";
    private const string NexusApiPathPrefix = "/api/v1/";

    private static readonly HeappeIdentifier HeappeMock = new("heappe-mock");

    private readonly RecordingCredentials _vault = new();
    private readonly ConcurrentQueue<string> _authenticateBodies = new();
    private readonly FakeHttpEndpoint _heappeBehindProxy = new();
    private readonly FakeTokenDelegationService _delegation = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private HeappeService? _heappeService;

    public HeappeConnectionFactoryTests()
    {
        _heappeBehindProxy.On(HttpMethod.Post, AuthenticateLexisTokenPath, async request =>
        {
            _authenticateBodies.Enqueue(await request.Content!.ReadAsStringAsync(Ct));
            return FakeHttpEndpoint.Json("\"session-1\"");
        });
        _heappeBehindProxy.On(HttpMethod.Get, ListAvailableClustersPath, _ => FakeHttpEndpoint.Json("[]"));
    }

    public void Dispose()
    {
        _heappeBehindProxy.Dispose();
        _cache.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForPublicAsync_addresses_the_instance_through_the_nexus_proxy(bool exchangeOn)
    {
        using var registry = NexusRegistryFake.Registry();
        var factory = exchangeOn ? FlagOnFactory(registry) : FlagOffFactory();

        var result = await factory.ForPublicAsync(HeappeMock, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, result.Value.BaseAddress);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForServiceAsync_reads_the_service_key_from_vault_by_identifier(bool exchangeOn)
    {
        using var registry = NexusRegistryFake.Registry();
        var factory = exchangeOn ? FlagOnFactory(registry) : FlagOffFactory();

        var result = await factory.ForServiceAsync(HeappeMock, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        var service = Assert.IsType<ServiceTokenHeappeConnectionInfo>(result.Value);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, service.BaseAddress);
        Assert.Equal("vault-key-of-heappe-mock", service.ServiceToken);
        Assert.Equal(["heappe-mock"], _vault.RequestedIdentifiers);
    }

    [Fact]
    public async Task ForServiceAsync_never_exchanges_a_token()
    {
        using var registry = NexusRegistryFake.Registry();
        await FlagOnFactory(registry).ForServiceAsync(HeappeMock, Ct);

        Assert.Empty(_delegation.Calls);
    }

    [Fact]
    public async Task ForUserAsync_with_token_forwards_the_callers_token_through_the_proxy_with_exchange_off()
    {
        var result = await FlagOffFactory().ForUserAsync(HeappeMock, "caller-token", useToken: true, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        var user = Assert.IsType<BearerTokenHeappeConnectionInfo>(result.Value);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, user.BaseAddress);
        Assert.Equal("caller-token", user.BearerToken);
    }

    [Fact]
    public async Task ForUserAsync_without_token_authenticates_the_caller_through_the_proxy_with_exchange_off()
    {
        var result = await FlagOffFactory().ForUserAsync(HeappeMock, "caller-token", useToken: false, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        var session = Assert.IsType<SessionCodeHEAppEConnectionInfo>(result.Value);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, session.BaseAddress);
        Assert.Equal("session-1", session.SessionCode);
        Assert.Equal(1, _heappeBehindProxy.CallsTo(AuthenticateLexisTokenPath));
    }

    [Fact]
    public async Task ForServiceAsync_reports_instance_not_found_without_asking_vault()
    {
        using var registry = NexusRegistryFake.Registry();
        var result = await FlagOnFactory(registry).ForServiceAsync(new HeappeIdentifier("unknown"), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.HeappeInstanceNotFound, result.Error.Code);
        Assert.Empty(_vault.RequestedIdentifiers);
    }

    [Fact]
    public async Task ForUserAsync_reports_instance_not_found_without_calling_heappe()
    {
        using var registry = NexusRegistryFake.Registry();
        var result = await FlagOnFactory(registry).ForUserAsync(new HeappeIdentifier("unknown"), CallerToken, useToken: false, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.HeappeInstanceNotFound, result.Error.Code);
        Assert.Empty(_heappeBehindProxy.RequestUris);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("heappe-off")]
    public async Task ForServiceAsync_with_exchange_off_routes_any_identifier_through_the_proxy_and_asks_vault(string identifier)
    {
        var result = await FlagOffFactory().ForServiceAsync(new HeappeIdentifier(identifier), Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(new Uri($"http://nexus.test/api/heappe/{identifier}/"), result.Value.BaseAddress);
        Assert.Equal([identifier], _vault.RequestedIdentifiers);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Qs09_ForUserAsync_with_exchange_off_sends_the_callers_token_unchanged_to_the_proxy_of_the_base_url(bool authHeader, bool useToken)
    {
        string[] flags = authHeader ? [AuthHeaderFlag] : [];

        var seen = await WhatHeappeSeesAsync(FlagOffFactory(flags), useToken);

        Assert.Contains(CallerToken, seen, StringComparison.Ordinal);
        Assert.DoesNotContain(ExchangedToken, seen, StringComparison.Ordinal);
        Assert.NotEmpty(_heappeBehindProxy.RequestUris);
        Assert.All(_heappeBehindProxy.RequestUris, uri =>
            Assert.StartsWith(NexusRegistryFake.HeappeMockProxyBase.AbsoluteUri, uri.AbsoluteUri, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Qs09_ForUserAsync_with_exchange_off_never_asks_the_registry_the_nexus_token_endpoint_or_the_idp(bool authHeader, bool useToken)
    {
        string[] flags = authHeader ? [AuthHeaderFlag] : [];

        await WhatHeappeSeesAsync(FlagOffFactory(flags), useToken);

        Assert.DoesNotContain(_heappeBehindProxy.RequestUris, uri => uri.AbsolutePath.StartsWith(NexusApiPathPrefix, StringComparison.Ordinal));
        Assert.Empty(_delegation.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Qs01_ForUserAsync_with_exchange_on_sends_only_the_exchanged_token_to_heappe(bool authHeader, bool useToken)
    {
        using var registry = NexusRegistryFake.Registry();
        string[] flags = authHeader ? [AuthHeaderFlag] : [];

        var seen = await WhatHeappeSeesAsync(FlagOnFactory(registry, flags), useToken);

        Assert.Contains(ExchangedToken, seen, StringComparison.Ordinal);
        Assert.DoesNotContain(CallerToken, seen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForUserAsync_with_exchange_on_hands_heappe_a_bearer_connection_with_the_exchanged_token()
    {
        using var registry = NexusRegistryFake.Registry();
        var result = await FlagOnFactory(registry).ForUserAsync(HeappeMock, CallerToken, useToken: true, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        var user = Assert.IsType<BearerTokenHeappeConnectionInfo>(result.Value);
        Assert.Equal(NexusRegistryFake.HeappeMockProxyBase, user.BaseAddress);
        Assert.Equal(ExchangedToken, user.BearerToken);
    }

    [Fact]
    public async Task ForUserAsync_exchanges_for_the_keycloak_client_and_scopes_nexus_lists_for_the_instance()
    {
        using var registry = NexusRegistryFake.Registry();
        await FlagOnFactory(registry).ForUserAsync(HeappeMock, CallerToken, useToken: true, Ct);

        var call = Assert.Single(_delegation.Calls);
        Assert.Equal(CallerToken, call.SubjectToken);
        Assert.NotNull(call.Options);
        Assert.Equal(HeappeClientId, call.Options.Audience);
        Assert.Equal("openid heappe", call.Options.Scope);
    }

    [Fact]
    public async Task ForUserAsync_does_not_exchange_when_the_instance_cannot_be_routed()
    {
        using var registry = NexusRegistryFake.Registry();
        await FlagOnFactory(registry).ForUserAsync(new HeappeIdentifier("unknown"), CallerToken, useToken: true, Ct);

        Assert.Empty(_delegation.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Qs05_ForUserAsync_returns_a_failed_exchange_as_an_error_result_without_calling_heappe(bool useToken)
    {
        using var registry = NexusRegistryFake.Registry();
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.Transient, DelegationClient, "idp unreachable"));

        var result = await FlagOnFactory(registry).ForUserAsync(HeappeMock, CallerToken, useToken, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        Assert.Equal(State.Error, result.State);
        Assert.Empty(_heappeBehindProxy.RequestUris);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForUserAsync_with_exchange_on_and_an_unconfigured_exchange_client_fails_without_sending_the_callers_token(bool useToken)
    {
        using var registry = NexusRegistryFake.Registry();
        var factory = FlagOnFactory(registry, exchange: new TokenExchangeOptions { ClientId = IntrospectionClientId });

        var result = await factory.ForUserAsync(HeappeMock, CallerToken, useToken, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        Assert.Empty(_delegation.Calls);
        Assert.Empty(_heappeBehindProxy.RequestUris);
        Assert.DoesNotContain(registry.Authorizations, a => a is not null && a.Contains(CallerToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForUserAsync_returns_an_instance_without_a_keycloak_client_as_token_exchange_unavailable()
    {
        using var registry = NexusRegistryFake.Registry(clientsJson: "[]");

        var result = await FlagOnFactory(registry).ForUserAsync(HeappeMock, CallerToken, useToken: false, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeUnavailable, result.Error.Code);
        Assert.Equal(State.Error, result.State);
        Assert.Empty(_heappeBehindProxy.RequestUris);
        Assert.Empty(_delegation.Calls);
    }

    [Fact]
    public async Task ForUserAsync_rethrows_the_callers_cancellation_during_the_exchange()
    {
        using var registry = NexusRegistryFake.Registry();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _delegation.Respond = async (_, _) =>
        {
            await caller.CancelAsync();
            throw new OperationCanceledException(caller.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await FlagOnFactory(registry).ForUserAsync(HeappeMock, CallerToken, useToken: true, caller.Token));
        Assert.Empty(_heappeBehindProxy.RequestUris);
    }

    private async Task<string> WhatHeappeSeesAsync(HeappeConnectionFactory factory, bool useToken)
    {
        var connection = await factory.ForUserAsync(HeappeMock, CallerToken, useToken, Ct);
        Assert.True(connection.IsSuccess, connection.IsSuccess ? null : connection.Error.Description);

        await _heappeService!.GetListAvailableClusters(connection.Value, ct: Ct);

        Assert.Equal(1, _heappeBehindProxy.CallsTo(ListAvailableClustersPath));
        return string.Join(
            " | ",
            _heappeBehindProxy.Authorizations.Select(a => a ?? string.Empty)
                .Concat(_authenticateBodies)
                .Concat(_heappeBehindProxy.RequestUris.Select(u => u.ToString())));
    }

    /// <summary>The composition with the flag on at startup: the registry directory and the real token exchanger.</summary>
    private HeappeConnectionFactory FlagOnFactory(FakeHttpEndpoint registry, string[]? enabledFlags = null, TokenExchangeOptions? exchange = null)
    {
        _heappeService = UserAuthentication(new FakeFeatureManager(enabledFlags ?? []));
        return new(NexusRegistryFake.Directory(registry), _vault, _heappeService, Exchanger(exchange));
    }

    /// <summary>The composition with the flag off at startup: the proxy-only directory and the pass-through exchanger.</summary>
    private HeappeConnectionFactory FlagOffFactory(params string[] enabledFlags)
    {
        _heappeService = UserAuthentication(new FakeFeatureManager(enabledFlags));
        return new(NexusRegistryFake.ProxyOnly(), _vault, _heappeService, new PassThroughTokenExchanger());
    }

    private HeappeTokenExchanger Exchanger(TokenExchangeOptions? exchange) =>
        new(
            _delegation,
            _cache,
            Options.Create(exchange ?? new TokenExchangeOptions
            {
                ClientId = IntrospectionClientId,
                ClientSecret = IntrospectionSecret,
                Authority = new Uri(TokenExchangeValues.Authority),
            }),
            TimeProvider.System,
            NullLogger<HeappeTokenExchanger>.Instance);

    private HeappeService UserAuthentication(IFeatureManager flags)
    {
        var authOptions = new Mock<IOptions<ClusterAuthTypeOptions>>();
        authOptions.Setup(x => x.Value).Returns(new ClusterAuthTypeOptions());

        return new HeappeService(
            NullLogger<HeappeService>.Instance,
            new FakeHttpClientFactory(_heappeBehindProxy),
            flags,
            new Mock<IDbContextFactory<HeappeIntegrationDbContext>>().Object,
            new Mock<IKillSwitchStateService>().Object,
            new Mock<IHeappeVersionResolver>().Object,
            authOptions.Object);
    }

    /// <summary>The Vault port: hands out a key per identifier and records which identifiers were asked for.</summary>
    private sealed class RecordingCredentials : ICredentialsService
    {
        private readonly ConcurrentQueue<string> _requested = new();

        public IReadOnlyCollection<string> RequestedIdentifiers => _requested;

        public Task<Result<HeappeCredential, Error>> GetCredentials(string heappeIdentifier, CancellationToken cancellationToken)
        {
            _requested.Enqueue(heappeIdentifier);
            return Task.FromResult(Result<HeappeCredential, Error>.Success(
                new HeappeCredential { SecretApiKey = $"vault-key-of-{heappeIdentifier}" },
                State.Ok));
        }
    }
}
