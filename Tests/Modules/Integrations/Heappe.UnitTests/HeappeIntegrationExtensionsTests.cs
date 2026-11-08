using System.Collections.Concurrent;
using System.Net;

using System.Diagnostics;
using System.Text;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Features.Connection;
using Heappe.Features.Credentials;
using Heappe.Features.Nexus;
using Heappe.Helpers;
using Heappe.HostContracts;
using Heappe.UnitTests.Features.Connection;
using Heappe.UnitTests.Features.Nexus;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FeatureManagement;

using Moq;

using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

using TokenDelegation.Interfaces;
using TokenDelegation.Models;

using UserOrg.Shared;

using Xunit;

using static Heappe.UnitTests.Features.Connection.TokenExchangeValues;

namespace Heappe.UnitTests;

/// <summary>
/// The HEAppE registration as the API host composes it. Every instance sits behind the same NexusApi host, reached
/// at <c>TokenExchange:Nexus:BaseUrl</c> (always required, an absolute http or https URI at the host root,
/// without path, userinfo, query or fragment), so
/// the data client's circuit breaker must be keyed by HEAppE identifier (one dead instance must not cut
/// off the others). <c>FeatureManagement:TokenExchangeEnabled</c> is read once at startup and gates the registrations:
/// off (or absent) registers only the proxy-only directory and the pass-through exchanger, so HEAppE calls go to
/// <c>{BaseUrl}/api/heappe/{identifier}/</c> with the caller's token and nothing reads the registry; on registers the
/// NexusApi registry and service account (<c>TokenExchange:Nexus:ClientId</c> and <c>ClientSecret</c>, then
/// required), TokenDelegation and the real exchanger. The token exchange (ADR-0118) is wired from the
/// <c>TokenExchange</c> section (<c>ClientId</c>, <c>ClientSecret</c>, <c>Authority</c>): with all three present the
/// delegation client posts to the realm's token endpoint through its own fail-fast pipeline, without them the
/// composition still resolves and one startup Warning names what is missing. A bad setting fails startup naming its
/// full key and never its value.
/// </summary>
public sealed class HeappeIntegrationExtensionsTests
{
    // HttpStandardResilienceOptions.CircuitBreaker.MinimumThroughput default: the breaker opens only after this
    // many outcomes in the sampling window.
    private const int BreakerMinimumThroughput = 100;

    private const string FlagKey = "FeatureManagement:TokenExchangeEnabled";
    private const string BaseUrlKey = "TokenExchange:Nexus:BaseUrl";
    private const string NexusClientIdKey = "TokenExchange:Nexus:ClientId";
    private const string NexusClientSecretKey = "TokenExchange:Nexus:ClientSecret";
    private const string DirectoryCacheTtlKey = "TokenExchange:Nexus:CacheMapTtl";
    private const string NexusBaseUrl = "http://nexus.test:5080/";
    private const string NexusServiceAccount = "nexus-sa-userorg-41f2";
    private const string NexusServiceAccountSecret = "nexus-sa-secret-3b9d";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddHEAppEIntegration_registers_the_connection_factory_and_the_instance_directory(bool tokenExchangeEnabled)
    {
        var builder = CreateBuilder(tokenExchangeEnabled);

        builder.AddHEAppEIntegration();

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IHeappeConnectionFactory));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IHeappeInstanceDirectory));
    }

    [Fact]
    public async Task Data_client_keeps_one_circuit_breaker_per_heappe_identifier()
    {
        var ct = TestContext.Current.CancellationToken;
        var builder = CreateBuilder();
        builder.AddHEAppEIntegration();
        builder.Services.AddHttpClient(HeappeIntegrationExtensions.HeappeClientName).ConfigurePrimaryHttpMessageHandler(() =>
            new RecordingHandler(request =>
                request.RequestUri!.AbsolutePath.Contains("/id-a/", StringComparison.Ordinal)
                    ? HttpStatusCode.InternalServerError
                    : HttpStatusCode.OK));
        using var provider = builder.Services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HeappeIntegrationExtensions.HeappeClientName);

        for (var i = 0; i < BreakerMinimumThroughput; i++)
        {
            using var failingRequest = HealthRequest("id-a");
            using var failing = await client.SendAsync(failingRequest, ct);
        }

        var deadInstance = await Record.ExceptionAsync(async () =>
        {
            using var request = HealthRequest("id-a");
            using var response = await client.SendAsync(request, ct);
        });
        Assert.IsType<BrokenCircuitException>(deadInstance);

        var healthyInstance = await Record.ExceptionAsync(async () =>
        {
            using var request = HealthRequest("id-b");
            using var response = await client.SendAsync(request, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
        Assert.Null(healthyInstance);
    }

    [Fact]
    public async Task Data_client_reaches_the_proxy_of_the_instance_under_TokenExchange_Nexus_BaseUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        var requestUris = new ConcurrentQueue<Uri>();
        var builder = CreateBuilder();
        AddHostServices(builder);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        builder.AddHEAppEIntegration();
        builder.Services.AddHttpClient(HeappeIntegrationExtensions.HeappeClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(_ => HttpStatusCode.OK, requestUris));
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var connection = await scope.ServiceProvider.GetRequiredService<IHeappeConnectionFactory>().ForPublicAsync(new HeappeIdentifier("heappe-mock"), ct);
        Assert.True(connection.IsSuccess, connection.IsSuccess ? null : connection.Error.Description);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HeappeIntegrationExtensions.HeappeClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(connection.Value.BaseAddress, "heappe/Health"));
        request.Options.Set(HeappeHttpRequestBuilder.HeappeIdentifierKey, "heappe-mock");
        using var response = await client.SendAsync(request, ct);

        Assert.Equal(new Uri("http://nexus.test:5080/api/heappe/heappe-mock/heappe/Health"), Assert.Single(requestUris));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_di_pipeline_resolves_the_connection_factory_in_a_request_scope(bool tokenExchangeEnabled)
    {
        var builder = CreateBuilder(tokenExchangeEnabled);
        AddHostServices(builder);
        builder.AddHEAppEIntegration();
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHeappeConnectionFactory>());
    }

    /// <summary>
    /// Characterization: an absolute http address sent through the HEAppE data client, with the service discovery
    /// UserOrg.ServiceDefaults puts on every client, reaches exactly that address.
    /// </summary>
    [Fact]
    public async Task Data_client_sends_a_plain_http_proxy_address_unchanged_through_service_discovery()
    {
        var requestUris = new ConcurrentQueue<Uri>();
        var builder = CreateBuilder();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        builder.AddHEAppEIntegration();
        builder.Services.AddHttpClient(HeappeIntegrationExtensions.HeappeClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(_ => HttpStatusCode.OK, requestUris));
        using var provider = builder.Services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HeappeIntegrationExtensions.HeappeClientName);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://nexus.test:5080/api/heappe/heappe-mock/heappe/Health"));
        request.Options.Set(HeappeHttpRequestBuilder.HeappeIdentifierKey, "heappe-mock");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(new Uri("http://nexus.test:5080/api/heappe/heappe-mock/heappe/Health"), Assert.Single(requestUris));
    }

    /// <summary>
    /// Characterization: the composed directory reads both registry lists with the NexusApi service-account token,
    /// which it gets once from <c>POST /api/v1/auth/token</c>. Since T-040 only with the flag on.
    /// </summary>
    [Fact]
    public async Task The_composed_directory_reads_the_registry_with_the_nexus_service_account_token()
    {
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Post, NexusServiceTokenProvider.TokenPath, _ => FakeHttpEndpoint.Json(ServiceAccountTokenJson));
        var builder = CreateBuilder(tokenExchangeEnabled: true);
        AddHostServices(builder);
        builder.AddHEAppEIntegration();
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => nexus));
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var route = await scope.ServiceProvider.GetRequiredService<IHeappeInstanceDirectory>()
            .ResolveAsync(new HeappeIdentifier("heappe-mock"), TestContext.Current.CancellationToken);

        Assert.True(route.IsSuccess, route.IsSuccess ? null : route.Error.Description);
        Assert.Equal(1, nexus.CallsTo(NexusServiceTokenProvider.TokenPath));
        Assert.Equal(1, nexus.CallsTo(NexusRegistryFake.HeappeInstancesPath));
        Assert.Equal(1, nexus.CallsTo(NexusRegistryFake.ClientsPath));
        Assert.Equal(2, nexus.Authorizations.Count(a => a == "Bearer service-account-token"));
    }

    [Fact]
    public async Task With_the_flag_on_the_registry_and_the_service_account_token_endpoint_are_reached_at_the_base_url()
    {
        var tokenRequestBodies = new ConcurrentQueue<string>();
        using var nexus = NexusRegistryFake.Registry()
            .On(HttpMethod.Post, NexusServiceTokenProvider.TokenPath, async request =>
            {
                tokenRequestBodies.Enqueue(await request.Content!.ReadAsStringAsync());
                return FakeHttpEndpoint.Json(ServiceAccountTokenJson);
            });
        var builder = CreateBuilder(tokenExchangeEnabled: true);
        AddHostServices(builder);
        builder.AddHEAppEIntegration();
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => nexus));
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var route = await scope.ServiceProvider.GetRequiredService<IHeappeInstanceDirectory>()
            .ResolveAsync(new HeappeIdentifier("heappe-mock"), TestContext.Current.CancellationToken);

        Assert.True(route.IsSuccess, route.IsSuccess ? null : route.Error.Description);
        Assert.Equal(new Uri("http://nexus.test:5080/api/heappe/heappe-mock/"), route.Value.ProxyBaseAddress);
        Assert.Contains(new Uri("http://nexus.test:5080/api/v1/auth/token"), nexus.RequestUris);
        Assert.Contains(new Uri("http://nexus.test:5080/api/v1/heappe"), nexus.RequestUris);
        Assert.Contains(new Uri("http://nexus.test:5080/api/v1/clients"), nexus.RequestUris);
        var tokenRequest = Assert.Single(tokenRequestBodies);
        Assert.Contains(NexusServiceAccount, tokenRequest, StringComparison.Ordinal);
        Assert.Contains(NexusServiceAccountSecret, tokenRequest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("2")]
    public void An_absent_empty_or_false_flag_registers_only_the_flag_off_set(string? flag)
    {
        var builder = CreateBuilder();
        builder.Configuration[FlagKey] = flag;
        AddHostServices(builder);
        builder.AddHEAppEIntegration();
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<PassThroughTokenExchanger>(Assert.Single(scope.ServiceProvider.GetServices<IHeappeTokenExchanger>()));
        Assert.IsType<ProxyOnlyHeappeInstanceDirectory>(Assert.Single(scope.ServiceProvider.GetServices<IHeappeInstanceDirectory>()));
        Assert.Null(provider.GetService<ITokenDelegationService>());
        Assert.Null(provider.GetService<NexusServiceTokenProvider>());
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(NexusRegistryClient));
        Assert.DoesNotContain(builder.Services, d => ImplementationTypeOf(d) == typeof(HeappeTokenExchanger));
        Assert.DoesNotContain(builder.Services, d => ImplementationTypeOf(d) == typeof(HeappeInstanceDirectory));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    public void A_true_flag_registers_the_flag_on_set_and_no_pass_through(string flag)
    {
        var builder = CreateBuilder(tokenExchangeEnabled: true);
        builder.Configuration[FlagKey] = flag;
        AddHostServices(builder);
        builder.AddHEAppEIntegration();
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<HeappeTokenExchanger>(Assert.Single(scope.ServiceProvider.GetServices<IHeappeTokenExchanger>()));
        Assert.IsType<HeappeInstanceDirectory>(Assert.Single(scope.ServiceProvider.GetServices<IHeappeInstanceDirectory>()));
        Assert.NotNull(provider.GetService<ITokenDelegationService>());
        Assert.NotNull(provider.GetService<NexusServiceTokenProvider>());
        Assert.DoesNotContain(builder.Services, d => ImplementationTypeOf(d) == typeof(PassThroughTokenExchanger));
        Assert.DoesNotContain(builder.Services, d => ImplementationTypeOf(d) == typeof(ProxyOnlyHeappeInstanceDirectory));
    }

    [Fact]
    public async Task Qs09_with_the_flag_off_a_user_connection_carries_the_callers_token_to_the_base_url_proxy_without_any_nexus_request()
    {
        var requestUris = new ConcurrentQueue<Uri>();
        var builder = CreateBuilder();
        AddHostServices(builder);
        builder.AddHEAppEIntegration();
        builder.Services.ConfigureHttpClientDefaults(http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(_ => HttpStatusCode.OK, requestUris)));
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var connection = await scope.ServiceProvider.GetRequiredService<IHeappeConnectionFactory>()
            .ForUserAsync(new HeappeIdentifier("heappe-mock"), CallerToken, useToken: true, TestContext.Current.CancellationToken);

        Assert.True(connection.IsSuccess, connection.IsSuccess ? null : connection.Error.Description);
        var user = Assert.IsType<BearerTokenHeappeConnectionInfo>(connection.Value);
        Assert.Equal(new Uri("http://nexus.test:5080/api/heappe/heappe-mock/"), user.BaseAddress);
        Assert.Equal(CallerToken, user.BearerToken);
        Assert.Empty(requestUris);
    }

    [Fact]
    public void Qs08_with_the_flag_off_the_base_url_alone_passes_startup_validation()
    {
        var failure = StartupFailureOf((NexusClientIdKey, null), (NexusClientSecretKey, null));

        Assert.Null(failure);
    }

    [Fact]
    public void With_the_flag_on_the_base_url_and_the_nexus_service_account_pass_startup_validation_without_an_exchange_client()
    {
        var failure = StartupFailureOf(FlagOn());

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(false, "http://nexus")]
    [InlineData(false, "https://nexus.example:8443/")]
    [InlineData(false, "http://nexus/")]
    [InlineData(false, "http://nexus/x")]
    [InlineData(true, "http://nexus")]
    [InlineData(true, "https://nexus.example:8443/")]
    [InlineData(true, "http://nexus/")]
    [InlineData(true, "http://nexus/nexus/")]
    public void An_absolute_http_or_https_base_url_passes_startup_validation(bool tokenExchangeEnabled, string baseAddress)
    {
        var failure = StartupFailureOf([.. Mode(tokenExchangeEnabled), (BaseUrlKey, baseAddress)]);

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Qs10_a_missing_base_url_fails_startup_naming_the_key(bool tokenExchangeEnabled)
    {
        var failure = StartupFailureOf([.. Mode(tokenExchangeEnabled), (BaseUrlKey, null)]);

        AssertStartupFailureNames(failure, BaseUrlKey);
        AssertCarriesNoServiceAccount(failure!);
    }

    [Theory]
    [InlineData(false, "nexus/")]
    [InlineData(false, "ftp://nexus")]
    [InlineData(false, "http://user:secret@nexus")]
    [InlineData(false, "http://nexus/?q=1")]
    [InlineData(false, "http://nexus/#f")]
    [InlineData(true, "nexus/")]
    [InlineData(true, "ftp://nexus")]
    [InlineData(true, "http://user:secret@nexus")]
    [InlineData(true, "http://nexus/?q=1")]
    [InlineData(true, "http://nexus/#f")]
    public void Qs10_an_invalid_base_url_fails_startup_naming_the_key_and_never_the_value(bool tokenExchangeEnabled, string baseAddress)
    {
        var failure = StartupFailureOf([.. Mode(tokenExchangeEnabled), (BaseUrlKey, baseAddress)]);

        AssertStartupFailureNames(failure, BaseUrlKey);
        var chain = failure!.ToString();
        Assert.DoesNotContain(baseAddress, chain, StringComparison.Ordinal);
        Assert.DoesNotContain("user:secret", chain, StringComparison.Ordinal);
        AssertCarriesNoServiceAccount(failure);
    }

    [Theory]
    [InlineData(NexusClientIdKey, null)]
    [InlineData(NexusClientIdKey, "")]
    [InlineData(NexusClientIdKey, "   ")]
    [InlineData(NexusClientSecretKey, null)]
    [InlineData(NexusClientSecretKey, "")]
    [InlineData(NexusClientSecretKey, "   ")]
    public void Qs10_with_the_flag_on_a_missing_or_blank_nexus_service_account_setting_fails_startup_naming_the_key(string key, string? value)
    {
        var failure = StartupFailureOf([.. FlagOn(), (key, value)]);

        AssertStartupFailureNames(failure, key);
        AssertCarriesNoServiceAccount(failure!);
        Assert.DoesNotContain(NexusBaseUrl, failure!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "00:00:00")]
    [InlineData(false, "02:00:00")]
    [InlineData(true, "00:00:00")]
    [InlineData(true, "02:00:00")]
    public void A_directory_cache_ttl_outside_one_second_to_one_hour_fails_startup_naming_the_key(bool tokenExchangeEnabled, string ttl)
    {
        var failure = StartupFailureOf([.. Mode(tokenExchangeEnabled), (DirectoryCacheTtlKey, ttl)]);

        AssertStartupFailureNames(failure, DirectoryCacheTtlKey);
        Assert.DoesNotContain(ttl, failure!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "00:00:01")]
    [InlineData(false, "01:00:00")]
    [InlineData(true, "00:00:01")]
    [InlineData(true, "01:00:00")]
    public void A_directory_cache_ttl_at_either_bound_passes_startup_validation(bool tokenExchangeEnabled, string ttl)
    {
        var failure = StartupFailureOf([.. Mode(tokenExchangeEnabled), (DirectoryCacheTtlKey, ttl)]);

        Assert.Null(failure);
    }

    [Theory]
    [InlineData("yes-please")]
    [InlineData("truthy")]
    public void A_flag_that_is_not_a_boolean_counts_as_off_and_startup_passes(string flag)
    {
        var failure = StartupFailureOf([.. FlagOn(), (FlagKey, flag)]);

        Assert.Null(failure);
    }

    [Fact]
    public void The_flag_in_its_EnabledFor_object_form_counts_as_off_and_startup_passes()
    {
        var failure = StartupFailureOf(
            ($"{FlagKey}:EnabledFor:0:Name", "AlwaysOn"));

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(Authority)]
    [InlineData(Authority + "/")]
    public void The_token_endpoint_is_built_from_the_authority_with_or_without_a_trailing_slash(string authority)
    {
        var builder = ExchangeBuilder(new LogSink(), new FakeIdentityProvider(), ("TokenExchange:Authority", authority));
        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TokenExchangeOptions>>().Value;

        Assert.Equal(new Uri(TokenEndpoint), options.TokenAuthorityEndpoint);
    }

    [Fact]
    public void The_exchange_client_credentials_come_from_the_TokenExchange_section()
    {
        var builder = ExchangeBuilder(new LogSink(), new FakeIdentityProvider());
        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TokenExchangeOptions>>().Value;

        Assert.Equal(IntrospectionClientId, options.ClientId);
        Assert.Equal(IntrospectionSecret, options.ClientSecret);
    }

    [Fact]
    public void The_nexus_settings_come_from_the_TokenExchange_Nexus_section_separate_from_the_exchange_client()
    {
        var builder = ExchangeBuilder(new LogSink(), new FakeIdentityProvider(), (DirectoryCacheTtlKey, "00:05:00"));
        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<TokenExchangeOptions>>().Value;

        Assert.Equal(new Uri(NexusBaseUrl), options.Nexus.BaseUrl);
        Assert.Equal(NexusServiceAccount, options.Nexus.ClientId);
        Assert.Equal(NexusServiceAccountSecret, options.Nexus.ClientSecret);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Nexus.CacheMapTtl);
        Assert.Equal(IntrospectionClientId, options.ClientId);
        Assert.Equal(IntrospectionSecret, options.ClientSecret);
    }

    [Theory]
    [InlineData("TokenExchange:ClientId", null)]
    [InlineData("TokenExchange:ClientId", "")]
    [InlineData("TokenExchange:ClientId", "   ")]
    [InlineData("TokenExchange:ClientSecret", null)]
    [InlineData("TokenExchange:ClientSecret", "")]
    [InlineData("TokenExchange:ClientSecret", "   ")]
    public void With_the_flag_on_a_missing_token_exchange_setting_fails_startup_naming_the_key(string key, string? value)
    {
        var failure = StartupFailureOf([.. FlagOn(), (key, value)]);

        AssertStartupFailureNames(failure, key);
        AssertCarriesNoSecret(failure!.ToString());
    }

    [Fact]
    public void With_the_flag_on_a_missing_authority_fails_startup_without_leaking_secrets()
    {
        var failure = StartupFailureOf([.. FlagOn(), ("TokenExchange:Authority", null)]);

        Assert.NotNull(failure);
        AssertCarriesNoSecret(failure.ToString());
    }

    [Fact]
    public void With_the_flag_off_the_token_exchange_section_is_not_required_and_only_the_nexus_base_url_is()
    {
        Assert.Null(StartupFailureOf());

        var failure = StartupFailureOf((BaseUrlKey, null));

        AssertStartupFailureNames(failure, BaseUrlKey);
    }

    [Fact]
    public void AddHEAppEIntegration_registers_the_scoped_token_exchanger_its_memory_cache_and_token_delegation()
    {
        var builder = ExchangeBuilder(new LogSink(), new FakeIdentityProvider());

        var exchanger = Assert.Single(builder.Services, d => d.ServiceType == typeof(IHeappeTokenExchanger));
        Assert.Equal(ServiceLifetime.Scoped, exchanger.Lifetime);
        Assert.Equal(typeof(HeappeTokenExchanger), exchanger.ImplementationType);
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IMemoryCache));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(ITokenDelegationService));
        Assert.DoesNotContain(builder.Services, d => ImplementationTypeOf(d) == typeof(PassThroughTokenExchanger));
    }

    [Fact]
    public void The_di_pipeline_resolves_the_exchanger_and_the_connection_factory()
    {
        var builder = ExchangeBuilder(new LogSink(), new FakeIdentityProvider());
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.IsType<HeappeTokenExchanger>(scope.ServiceProvider.GetRequiredService<IHeappeTokenExchanger>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHeappeConnectionFactory>());
    }

    [Fact]
    public async Task With_credentials_the_exchange_posts_audience_scope_and_subject_token_to_the_token_endpoint()
    {
        var idp = new FakeIdentityProvider();
        using var provider = ExchangeBuilder(new LogSink(), idp).Services.BuildServiceProvider();

        var result = await ExchangeAsync(provider);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(ExchangedToken, result.Value);
        var request = Assert.Single(idp.Requests);
        Assert.Equal(new Uri(TokenEndpoint), request.Uri);
        var form = QueryHelpers.ParseQuery(request.Body);
        Assert.Equal(HeappeClientId, form["audience"].ToString());
        Assert.Equal("openid heappe", form["scope"].ToString());
        Assert.Equal(CallerToken, form["subject_token"].ToString());
        Assert.True(
            form["client_id"].ToString() == IntrospectionClientId || BasicCredentialsOf(request.Authorization) == $"{IntrospectionClientId}:{IntrospectionSecret}",
            "The exchange must authenticate as the introspection client.");
    }

    [Fact]
    public void Qs04_the_delegation_client_has_a_fail_fast_pipeline_with_one_attempt_and_a_breaker()
    {
        using var provider = ExchangeBuilder(new LogSink(), new FakeIdentityProvider()).Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"tokendelegation:{DelegationClient}-standard");

        Assert.Equal(TimeSpan.FromSeconds(5), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.TotalRequestTimeout.Timeout);
        Assert.Equal(5, options.CircuitBreaker.MinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(10), options.CircuitBreaker.SamplingDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CircuitBreaker.BreakDuration);
    }

    [Fact]
    public async Task Qs04_the_delegation_client_never_retries()
    {
        using var provider = ExchangeBuilder(new LogSink(), new FakeIdentityProvider()).Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"tokendelegation:{DelegationClient}-standard");
        var context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        using var unavailable = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        var retriesAResponse = await options.Retry.ShouldHandle(new RetryPredicateArguments<HttpResponseMessage>(context, Outcome.FromResult(unavailable), 0));
        var retriesAnException = await options.Retry.ShouldHandle(new RetryPredicateArguments<HttpResponseMessage>(context, Outcome.FromException<HttpResponseMessage>(new HttpRequestException("refused")), 0));
        ResilienceContextPool.Shared.Return(context);

        Assert.False(retriesAResponse);
        Assert.False(retriesAnException);
    }

    [Fact]
    public async Task Qs04_an_unavailable_idp_is_asked_once_even_with_the_inherited_default_pipeline()
    {
        var idp = new FakeIdentityProvider { Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)) };
        var builder = ExchangeBuilderWith(new LogSink(), idp, inheritDefaults: true);
        using var provider = builder.Services.BuildServiceProvider();

        var result = await ExchangeAsync(provider);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        Assert.Single(idp.Requests);
    }

    [Fact]
    public async Task Qs04_a_slow_idp_is_given_up_after_the_5_second_attempt_timeout()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var idp = new FakeIdentityProvider
        {
            Respond = async ct =>
            {
                arrived.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return FakeIdentityProvider.Granted();
            }
        };
        var builder = ExchangeBuilderWith(new LogSink(), idp, time: time);
        using var provider = builder.Services.BuildServiceProvider();

        var exchange = ExchangeAsync(provider);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(5));
        var result = await exchange.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        Assert.Single(idp.Requests);
    }

    [Fact]
    public async Task Qs04_with_the_breaker_open_the_exchange_fails_in_under_100_ms_without_an_idp_call()
    {
        var idp = new FakeIdentityProvider { Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)) };
        using var provider = ExchangeBuilder(new LogSink(), idp).Services.BuildServiceProvider();
        for (var i = 0; i < 5; i++)
        {
            await ExchangeAsync(provider);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await ExchangeAsync(provider);
        stopwatch.Stop();

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        Assert.Equal(5, idp.Requests.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100), $"took {stopwatch.Elapsed.TotalMilliseconds} ms");
    }

    [Fact]
    public async Task Qs02_a_refused_exchange_through_the_real_pipeline_leaks_no_token_or_secret()
    {
        var logs = new LogSink();
        var idp = new FakeIdentityProvider
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    $$"""{"error":"invalid_grant","error_description":"subject {{CallerToken}} refused, issued {{ExchangedToken}}, client secret {{IntrospectionSecret}}"}""",
                    Encoding.UTF8,
                    "application/json")
            })
        };
        using var provider = ExchangeBuilder(logs, idp).Services.BuildServiceProvider();

        var result = await ExchangeAsync(provider);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        AssertCarriesNoSecret(result.Error.Description);
        Assert.NotEmpty(logs.Records);
        foreach (var record in logs.Records)
        {
            Assert.False(CarriesSecret(record.Text), $"A {record.Level} record of {record.Category} carries a token or secret.");
            Assert.False(CarriesSecret(record.Exception?.ToString() ?? string.Empty), $"The exception of a {record.Level} record of {record.Category} carries a token or secret.");
        }

        foreach (var key in provider.GetRequiredService<IMemoryCache>() is MemoryCache cache ? cache.Keys.Select(k => k.ToString() ?? string.Empty) : [])
        {
            AssertCarriesNoSecret(key);
        }
    }

    [Fact]
    public async Task Qs06_a_refusing_idp_yields_one_exchanger_warning_per_minute_and_no_exchanger_error()
    {
        var logs = new LogSink();
        var idp = new FakeIdentityProvider
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":"access_denied"}""", Encoding.UTF8, "application/json")
            })
        };
        using var provider = ExchangeBuilder(logs, idp).Services.BuildServiceProvider();

        await ExchangeAsync(provider);
        await ExchangeAsync(provider);
        await ExchangeAsync(provider);

        var exchangerRecords = logs.Records.Where(r => r.Category.Contains(nameof(HeappeTokenExchanger), StringComparison.Ordinal)).ToList();
        var warning = Assert.Single(exchangerRecords, r => r.Level == LogLevel.Warning);
        Assert.Contains("heappe-mock", warning.Text, StringComparison.Ordinal);
        Assert.Contains(HeappeFailureCause.TokenExchangeFailed, warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(exchangerRecords, r => r.Level >= LogLevel.Error);
    }

    private const string ServiceAccountTokenJson = """{"access_token":"service-account-token","token_type":"Bearer","expires_in":300}""";

    private static readonly string[] CredentialNames = ["ClientId", "ClientSecret", "Authority"];

    private static readonly HeappeRoute HeappeMockRoute = new(
        new HeappeIdentifier("heappe-mock"),
        new Uri("http://nexus.test:5080/api/heappe/heappe-mock/"),
        HeappeClientId,
        ["openid", "heappe"]);

    private static async Task<Lukdrasil.Result.Result<string, Lukdrasil.Error.Error>> ExchangeAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IHeappeTokenExchanger>()
            .ExchangeAsync(HeappeMockRoute, CallerToken, TestContext.Current.CancellationToken);
    }

    private static (string Key, string? Value)[] FlagOn() =>
    [
        (FlagKey, "true"),
        ("TokenExchange:Authority", Authority),
        ("TokenExchange:ClientId", IntrospectionClientId),
        ("TokenExchange:ClientSecret", IntrospectionSecret),
        (NexusClientIdKey, NexusServiceAccount),
        (NexusClientSecretKey, NexusServiceAccountSecret),
    ];

    private static (string Key, string? Value)[] Mode(bool tokenExchangeEnabled) => tokenExchangeEnabled ? FlagOn() : [];

    /// <summary>
    /// Composes the integration over <see cref="CreateBuilder"/> plus <paramref name="settings"/> and runs startup
    /// validation the way the host does (<see cref="IStartupValidator"/>); <c>BuildServiceProvider</c> alone would not.
    /// A failure anywhere in composition or validation is the startup failure.
    /// </summary>
    private static Exception? StartupFailureOf(params (string Key, string? Value)[] settings)
    {
        var builder = CreateBuilder();
        foreach (var (key, value) in settings)
        {
            builder.Configuration[key] = value;
        }

        AddHostServices(builder);
        return Record.Exception(() =>
        {
            builder.AddHEAppEIntegration();
            using var provider = builder.Services.BuildServiceProvider();
            provider.GetRequiredService<IStartupValidator>().Validate();
        });
    }

    private static void AssertStartupFailureNames(Exception? failure, string key)
    {
        Assert.NotNull(failure);
        var text = failure.ToString();
        var split = key.LastIndexOf(':');
        var sectionAndProperty = $"{key[..split]}: '{key[(split + 1)..]}'";
        Assert.True(
            text.Contains(key, StringComparison.Ordinal) || text.Contains(sectionAndProperty, StringComparison.Ordinal),
            $"The startup failure does not name '{key}'.");
    }

    private static void AssertCarriesNoServiceAccount(Exception failure)
    {
        Assert.DoesNotContain(NexusServiceAccount, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(NexusServiceAccountSecret, failure.ToString(), StringComparison.Ordinal);
    }

    private static void AddHostServices(HostApplicationBuilder builder)
    {
        builder.Configuration["Heappe:ClusterAuthTypes:AllowedAuthenticationTypeNames:0"] = "PasswordAuthentication";
        builder.Services.AddHybridCache();
        builder.Services.AddFeatureManagement();
        builder.Services.AddScoped(_ => Mock.Of<ICredentialsService>());
        builder.Services.AddScoped(_ => Mock.Of<IHeappeVersionSource>());
    }

    private static HostApplicationBuilder ExchangeBuilder(
        LogSink logs,
        FakeIdentityProvider idp,
        params (string Key, string? Value)[] overrides) =>
        ExchangeBuilderWith(logs, idp, inheritDefaults: false, time: null, overrides);

    private static HostApplicationBuilder ExchangeBuilderWith(
        LogSink logs,
        FakeIdentityProvider idp,
        bool inheritDefaults = false,
        TimeProvider? time = null,
        params (string Key, string? Value)[] overrides)
    {
        var builder = CreateBuilder(tokenExchangeEnabled: true);
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Services.AddSingleton<ILoggerProvider>(_ => new RecordingLoggerProvider(logs));
        builder.Configuration["TokenExchange:Authority"] = Authority;
        builder.Configuration["TokenExchange:ClientId"] = IntrospectionClientId;
        builder.Configuration["TokenExchange:ClientSecret"] = IntrospectionSecret;
        foreach (var (key, value) in overrides)
        {
            builder.Configuration[key] = value;
        }

        if (time is not null)
        {
            builder.Services.AddSingleton(time);
        }

        if (inheritDefaults)
        {
            builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        }

        AddHostServices(builder);
        builder.AddHEAppEIntegration();
        builder.Services.AddHttpClient($"tokendelegation:{DelegationClient}").ConfigurePrimaryHttpMessageHandler(idp.Handler);
        return builder;
    }

    private static IEnumerable<ServiceDescriptor> HostedServicesOnlyIn(HostApplicationBuilder unconfigured, HostApplicationBuilder configured)
    {
        var known = configured.Services.Where(d => d.ServiceType == typeof(IHostedService)).Select(Identity).ToList();
        return unconfigured.Services.Where(d => d.ServiceType == typeof(IHostedService) && !known.Remove(Identity(d)));
    }

    private static string Identity(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName
        ?? descriptor.ImplementationInstance?.GetType().FullName
        ?? descriptor.ImplementationFactory?.Method.ToString()
        ?? string.Empty;

    /// <summary>
    /// The type a descriptor produces: its implementation type or instance, or for a factory registration the result
    /// type of the factory delegate (<c>AddHostedService(sp => new T(...))</c> registers a <c>Func&lt;IServiceProvider, T&gt;</c>).
    /// </summary>
    private static Type? ImplementationTypeOf(ServiceDescriptor descriptor) =>
        descriptor.IsKeyedService
            ? null
            : descriptor.ImplementationType
              ?? descriptor.ImplementationInstance?.GetType()
              ?? descriptor.ImplementationFactory?.GetType().GenericTypeArguments.LastOrDefault();

    private static IHostedService Instantiate(IServiceProvider provider, ServiceDescriptor descriptor) =>
        (IHostedService)(descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));

    private static string? BasicCredentialsOf(string? authorization) =>
        authorization is not null && authorization.StartsWith("Basic ", StringComparison.Ordinal)
            ? Uri.UnescapeDataString(Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..])))
            : null;

    private static bool CarriesSecret(string text) =>
        text.Contains(CallerToken, StringComparison.Ordinal)
        || text.Contains(ExchangedToken, StringComparison.Ordinal)
        || text.Contains(IntrospectionSecret, StringComparison.Ordinal);

    private static void AssertCarriesNoSecret(string text)
    {
        Assert.DoesNotContain(CallerToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ExchangedToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(IntrospectionSecret, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host builder with the persistence connection string and <c>TokenExchange:Nexus:BaseUrl</c>; with
    /// <paramref name="tokenExchangeEnabled"/> also the flag on and the NexusApi service account it then requires.
    /// </summary>
    private static HostApplicationBuilder CreateBuilder(bool tokenExchangeEnabled = false)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.Configuration["ConnectionStrings:UserOrgDBContext"] =
            "Server=(local);Database=fake;Trusted_Connection=True;TrustServerCertificate=True;";
        builder.Configuration[BaseUrlKey] = NexusBaseUrl;
        foreach (var (key, value) in Mode(tokenExchangeEnabled))
        {
            builder.Configuration[key] = value;
        }

        return builder;
    }

    private static HttpRequestMessage HealthRequest(string identifier)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://nexus.test/api/heappe/{identifier}/heappe/Health"));
        request.Options.Set(HeappeHttpRequestBuilder.HeappeIdentifierKey, identifier);
        return request;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpStatusCode> statusFor, ConcurrentQueue<Uri>? requestUris = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requestUris?.Enqueue(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(statusFor(request)) { Content = new StringContent("{}") });
        }
    }
}
