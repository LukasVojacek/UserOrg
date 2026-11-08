using System.Net;
using System.Net.Http.Headers;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Authentication;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins what <see cref="TokenIntrospectionService.IntrospectAsync"/> answers and sends for an enabled
/// introspection (RFC 7662): a repeated token is served from the cache, client credentials go out as
/// Basic auth and their absence as an anonymous call, and every failure - no endpoint, an HTTP error,
/// a body that is not JSON - fails closed as inactive.
/// </summary>
public sealed class TokenIntrospectionServiceTests
{
    private const string IntrospectionClientName = "IntrospectionBackChannel";
    private const string Authority = "https://idp.test/realms/master";
    private const string Endpoint = "https://idp.test/realms/master/protocol/openid-connect/token/introspect";
    private const string ClientId = "gateway";
    private const string ClientSecret = "introspection-secret";
    private const string Token = "opaque-token";
    private const string ActiveResponse = """{"active":true,"role":["role-a","role-b"]}""";

    [Fact]
    public async Task Should_ServeTheSecondCallFromTheCache_When_TheSameTokenIsIntrospectedTwice()
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithEndpoint());
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);
        var cached = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.Single(endpoint.Requests);
        Assert.True(cached.Active);
        Assert.Equal(["role-a", "role-b"], cached.Claims["role"]);
    }

    [Fact]
    public async Task Should_SendTheClientCredentialsAsBasicAuth_When_ClientIdAndSecretAreConfigured()
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        var configuration = WithEndpoint();
        configuration["Idp:Introspection:ClientId"] = ClientId;
        configuration["Idp:Introspection:ClientSecret"] = ClientSecret;
        using var provider = IntrospectionServices(endpoint, configuration);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        var request = Assert.Single(endpoint.Requests);
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        Assert.Equal(new AuthenticationHeaderValue("Basic", expected), request.Authorization);
        Assert.Equal($"token={Token}", request.Body);
        Assert.True(result.Active);
    }

    [Fact]
    public async Task Should_SendNoAuthorizationHeader_When_NoClientCredentialsAreConfigured()
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithEndpoint());
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        var request = Assert.Single(endpoint.Requests);
        Assert.Null(request.Authorization);
        Assert.True(result.Active);
    }

    [Fact]
    public async Task Should_ReturnInactiveWithoutACall_When_NoEndpointCanBeResolved()
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, new Dictionary<string, string?> { ["Idp:Introspection:Enabled"] = "true" });
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.False(result.Active);
        Assert.Empty(endpoint.Requests);
    }

    [Fact]
    public async Task Should_ReturnInactive_When_TheEndpointAnswersWithAnHttpError()
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.InternalServerError, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithEndpoint());
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.False(result.Active);
        Assert.Single(endpoint.Requests);
    }

    [Fact]
    public async Task Should_ReturnInactive_When_TheEndpointAnswersWithInvalidJson()
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, "not json");
        using var provider = IntrospectionServices(endpoint, WithEndpoint());
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.False(result.Active);
        Assert.Single(endpoint.Requests);
    }

    [Fact]
    public void Should_LogTheDiscoveryAuthorityAndAnonymousMode_When_OnlyTheAuthorityIsConfigured()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new DiscoveringEndpoint(HttpStatusCode.OK, DiscoveryWith(Endpoint), ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithAuthority(), logs);

        ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        Assert.Equal(
            new[]
            {
                (LogLevel.Information, $"[INTROSPECTION] Introspection endpoint will be discovered from {Authority}"),
                (LogLevel.Information, "[INTROSPECTION] Using ANONYMOUS mode (no client credentials configured)"),
            },
            logs.From<TokenIntrospectionService>().Select(entry => (entry.Level, entry.Message)));
    }

    [Fact]
    public void Should_LogOnlyTheBasicAuthMode_When_TheEndpointAndCredentialsAreConfigured()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        var configuration = WithEndpoint();
        configuration["Idp:Authority"] = Authority;
        configuration["Idp:Introspection:ClientId"] = ClientId;
        configuration["Idp:Introspection:ClientSecret"] = ClientSecret;
        using var provider = IntrospectionServices(endpoint, configuration, logs);

        ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var only = Assert.Single(logs.From<TokenIntrospectionService>());
        Assert.Equal(LogLevel.Information, only.Level);
        Assert.Equal("[INTROSPECTION] Using BASIC AUTH mode (client credentials configured)", only.Message);
    }

    [Fact]
    public void Should_LogAnonymousModeWithoutADiscoveryLine_When_NeitherEndpointNorAuthorityIsConfigured()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, new Dictionary<string, string?> { ["Idp:Introspection:ClientId"] = ClientId }, logs);

        ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var only = Assert.Single(logs.From<TokenIntrospectionService>());
        Assert.Equal("[INTROSPECTION] Using ANONYMOUS mode (no client credentials configured)", only.Message);
    }

    [Fact]
    public void Should_Throw_When_TheEnabledFlagIsNotABoolean()
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, new Dictionary<string, string?> { ["Idp:Introspection:Enabled"] = "maybe" });

        Assert.Throws<FormatException>(() => ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider));
    }

    [Fact]
    public async Task Should_IntrospectAtTheDiscoveredEndpoint_When_TheDiscoveryDocumentAdvertisesOne()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new DiscoveringEndpoint(HttpStatusCode.OK, DiscoveryWith(Endpoint), ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithAuthority(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.True(result.Active);
        Assert.Equal([$"GET {Authority}/.well-known/openid-configuration", $"POST {Endpoint}"], endpoint.Requests);
        Assert.Contains(logs.From<TokenIntrospectionService>(), entry =>
            entry.Level == LogLevel.Information && entry.Message == $"[INTROSPECTION] Discovered introspection endpoint: {Endpoint}");
    }

    [Fact]
    public async Task Should_TrimTheTrailingSlashOfTheAuthority_When_BuildingTheDiscoveryUrl()
    {
        using var endpoint = new DiscoveringEndpoint(HttpStatusCode.OK, DiscoveryWith(Endpoint), ActiveResponse);
        using var provider = IntrospectionServices(endpoint, new Dictionary<string, string?> { ["Idp:Authority"] = Authority + "/" });
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.Equal($"GET {Authority}/.well-known/openid-configuration", endpoint.Requests[0]);
    }

    [Fact]
    public async Task Should_ReturnInactiveWithAWarning_When_TheDiscoveryDocumentHasNoIntrospectionEndpoint()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new DiscoveringEndpoint(HttpStatusCode.OK, """{"issuer":"x"}""", ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithAuthority(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.False(result.Active);
        Assert.Single(endpoint.Requests);
        var entries = logs.From<TokenIntrospectionService>();
        Assert.Contains(entries, entry => entry.Level == LogLevel.Warning && entry.Message == "[INTROSPECTION] Discovery document does not contain introspection_endpoint");
        Assert.Contains(entries, entry => entry.Level == LogLevel.Error && entry.Message == "[INTROSPECTION] Introspection endpoint not configured and could not be discovered");
    }

    [Fact]
    public async Task Should_ReturnInactiveWithAnError_When_TheDiscoveryCallFails()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new DiscoveringEndpoint(HttpStatusCode.ServiceUnavailable, DiscoveryWith(Endpoint), ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithAuthority(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.False(result.Active);
        Assert.Single(endpoint.Requests);
        Assert.Contains(logs.From<TokenIntrospectionService>(), entry =>
            entry.Level == LogLevel.Error && entry.Message == $"[INTROSPECTION] Failed to discover introspection endpoint from {Authority}");
    }

    [Fact]
    public async Task Should_ReturnInactiveWithAnError_When_TheIntrospectionCallFailsWithAnHttpError()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new RecordingEndpoint(HttpStatusCode.InternalServerError, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithEndpoint(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.Contains(logs.From<TokenIntrospectionService>(), entry =>
            entry.Level == LogLevel.Error && entry.Message == "[INTROSPECTION] HTTP request failed when calling introspection endpoint");
    }

    [Fact]
    public async Task Should_ReturnInactiveWithAnError_When_TheIntrospectionBodyIsNotJson()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, "not json");
        using var provider = IntrospectionServices(endpoint, WithEndpoint(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.Contains(logs.From<TokenIntrospectionService>(), entry =>
            entry.Level == LogLevel.Error && entry.Message == "[INTROSPECTION] Unexpected error during introspection");
    }

    [Fact]
    public async Task Should_KeepStringAndStringArrayClaimsOnly_When_TheResponseMixesValueKinds()
    {
        const string response = """
            {
              "active": true,
              "exp": 4102444800,
              "sub": "user-1",
              "empty": "",
              "scope_list": ["a", 1, "", "b", null],
              "numbers": [1, 2],
              "count": 7,
              "flag": false,
              "nested": {"x": "y"}
            }
            """;
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, response);
        using var provider = IntrospectionServices(endpoint, WithEndpoint());
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.True(result.Active);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(4102444800), result.Exp);
        Assert.Equal(["scope_list", "sub"], result.Claims.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["user-1"], result.Claims["SUB"]);
        Assert.Equal(["a", "b"], result.Claims["scope_list"]);
    }

    [Theory]
    [InlineData("""{"exp":4102444800}""")]
    [InlineData("""{"active":false,"exp":4102444800}""")]
    public async Task Should_ReportInactive_When_TheResponseIsNotActive(string response)
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, response);
        using var provider = IntrospectionServices(endpoint, WithEndpoint());
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.False(result.Active);
        Assert.Empty(result.Claims);
    }

    [Theory]
    [InlineData("""{"active":true}""")]
    [InlineData("""{"active":true,"exp":"4102444800"}""")]
    [InlineData("""{"active":true,"exp":1.5}""")]
    public async Task Should_LeaveTheExpiryUnset_When_TheExpIsMissingOrNotAnInteger(string response)
    {
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, response);
        using var provider = IntrospectionServices(endpoint, WithEndpoint());
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        var result = await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        Assert.True(result.Active);
        Assert.Null(result.Exp);
        Assert.Empty(result.Claims);
    }

    [Theory]
    [InlineData("""{"active":true}""", "60")]
    [InlineData("""{"active":true,"exp":1}""", "5")]
    [InlineData("""{"active":true,"exp":4102444800}""", "300")]
    public async Task Should_CacheForTheTtlTheExpiryAllows_When_TheIntrospectionSucceeds(string response, string expectedSeconds)
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, response);
        using var provider = IntrospectionServices(endpoint, WithEndpoint(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        var succeeded = Assert.Single(logs.From<TokenIntrospectionService>(), entry => entry.Message.StartsWith("[INTROSPECTION] Token introspection successful", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Debug, succeeded.Level);
        Assert.Equal($"[INTROSPECTION] Token introspection successful, active=True, cached for {expectedSeconds}s", succeeded.Message);
    }

    [Fact]
    public async Task Should_CacheForTheRemainingLifetime_When_TheTokenExpiresWithinFiveMinutes()
    {
        using var logs = new CapturingLoggerProvider();
        var exp = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds();
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, $$"""{"active":true,"exp":{{exp}}}""");
        using var provider = IntrospectionServices(endpoint, WithEndpoint(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        var succeeded = Assert.Single(logs.From<TokenIntrospectionService>(), entry => entry.Message.StartsWith("[INTROSPECTION] Token introspection successful", StringComparison.Ordinal));
        var seconds = double.Parse(succeeded.Message.Split("cached for ")[1].TrimEnd('s'), System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 100, 121);
    }

    [Fact]
    public async Task Should_LogTheCacheMissAndHit_When_TheSameTokenIsIntrospectedTwice()
    {
        using var logs = new CapturingLoggerProvider();
        using var endpoint = new RecordingEndpoint(HttpStatusCode.OK, ActiveResponse);
        using var provider = IntrospectionServices(endpoint, WithEndpoint(), logs);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);
        await introspection.IntrospectAsync(Token, TestContext.Current.CancellationToken);

        var entries = logs.From<TokenIntrospectionService>();
        Assert.Contains(entries, entry => entry.Level == LogLevel.Debug && entry.Message == "[INTROSPECTION] Cache miss, calling introspection endpoint (anonymous)");
        Assert.Contains(entries, entry => entry.Level == LogLevel.Debug && entry.Message == "[INTROSPECTION] Cache hit for token");
    }

    private static Dictionary<string, string?> WithAuthority() => new()
    {
        ["Idp:Authority"] = Authority,
    };

    private static string DiscoveryWith(string introspectionEndpoint) =>
        $$"""{"issuer":"{{Authority}}","introspection_endpoint":"{{introspectionEndpoint}}"}""";

    private static Dictionary<string, string?> WithEndpoint() => new()
    {
        ["Idp:Introspection:Enabled"] = "true",
        ["Idp:Introspection:Endpoint"] = Endpoint,
    };

    private static ServiceProvider IntrospectionServices(HttpMessageHandler endpoint, Dictionary<string, string?> configuration, CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace);
            }
        });
        services.AddHttpClient(IntrospectionClientName).ConfigurePrimaryHttpMessageHandler(() => endpoint);
        services.AddDistributedMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        services.AddSingleton(Mock.Of<IHostEnvironment>(host => host.EnvironmentName == Environments.Production));
        return services.BuildServiceProvider();
    }

    private sealed record RecordedRequest(AuthenticationHeaderValue? Authorization, string Body);

    private sealed class RecordingEndpoint(HttpStatusCode status, string responseBody) : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];

        public IReadOnlyList<RecordedRequest> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Add(new RecordedRequest(request.Headers.Authorization, body));
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>
    /// Answers the discovery GET with <paramref name="discoveryStatus"/> and <paramref name="discoveryBody"/>
    /// and every POST with the introspection body; records each request as "METHOD url".
    /// </summary>
    private sealed class DiscoveringEndpoint(HttpStatusCode discoveryStatus, string discoveryBody, string introspectionBody) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            var response = request.Method == HttpMethod.Get
                ? new HttpResponseMessage(discoveryStatus) { Content = new StringContent(discoveryBody, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(introspectionBody, Encoding.UTF8, "application/json") };
            return Task.FromResult(response);
        }
    }
}
