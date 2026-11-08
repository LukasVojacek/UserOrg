using System.Net;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Features.Diagnostics;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the Development-only <c>/_diagnostics/oidc</c> report: one plain-text page describing the
/// Bearer options, the discovered OIDC document, its signing keys, and the token introspection settings,
/// and an inline error section (plus an Error log) when the discovery call fails with an HTTP error.
/// </summary>
public sealed class OidcDiagnosticsEndpointTests
{
    private static readonly Uri Path = new("/_diagnostics/oidc", UriKind.Relative);
    private const string Authority = "https://idp.diagnostics.example/realms/test";
    private const string DiscoveredIntrospection = "https://idp.diagnostics.example/introspect";

    [Fact]
    public async Task Should_AnswerAnonymousPlainText_When_TheDiagnosticsPathIsRequested()
    {
        await using var app = await StartAsync(configureBearer: null);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(Path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("=== OIDC Configuration Diagnostics ===", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportTheBearerOptionsAndValidationParameters_When_TheBearerSchemeIsConfigured()
    {
        var body = await GetReportAsync(options =>
        {
            options.Authority = Authority;
            options.RequireHttpsMetadata = false;
            options.SaveToken = true;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidIssuer = "issuer-x";
            options.TokenValidationParameters.ValidAudience = "audience-y";
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(42);
            options.ConfigurationManager = Manager(new OpenIdConnectConfiguration());
        });

        Assert.Contains($"Authority: {Authority}", body, StringComparison.Ordinal);
        Assert.Contains("RequireHttpsMetadata: False", body, StringComparison.Ordinal);
        Assert.Contains("SaveToken: True", body, StringComparison.Ordinal);
        Assert.Contains("MapInboundClaims: False", body, StringComparison.Ordinal);
        Assert.Contains("  ValidIssuer: issuer-x", body, StringComparison.Ordinal);
        Assert.Contains("  ValidAudience: audience-y", body, StringComparison.Ordinal);
        Assert.Contains("  ClockSkew: 00:00:42", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportTheMissingManager_When_TheBearerOptionsHaveNoConfigurationManager()
    {
        var body = await GetReportAsync(configureBearer: null);

        Assert.Contains("? ConfigurationManager is null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Attempting to retrieve OIDC configuration...", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportTheDiscoveredDocument_When_TheManagerReturnsAConfiguration()
    {
        var configuration = new OpenIdConnectConfiguration
        {
            Issuer = "https://issuer.example",
            JwksUri = "https://issuer.example/jwks",
            AuthorizationEndpoint = "https://issuer.example/auth",
            TokenEndpoint = "https://issuer.example/token",
        };
        configuration.ResponseTypesSupported.Add("code");
        configuration.ScopesSupported.Add("openid");
        configuration.ScopesSupported.Add("profile");

        var body = await GetReportAsync(options => options.ConfigurationManager = Manager(configuration));

        Assert.Contains("Attempting to retrieve OIDC configuration...", body, StringComparison.Ordinal);
        Assert.Contains("? OIDC Configuration retrieved successfully", body, StringComparison.Ordinal);
        Assert.Contains("  Issuer: https://issuer.example", body, StringComparison.Ordinal);
        Assert.Contains("  JwksUri: https://issuer.example/jwks", body, StringComparison.Ordinal);
        Assert.Contains("  Authorization Endpoint: https://issuer.example/auth", body, StringComparison.Ordinal);
        Assert.Contains("  Token Endpoint: https://issuer.example/token", body, StringComparison.Ordinal);
        Assert.Contains($"Supported Response Types:{Environment.NewLine}  - code", body, StringComparison.Ordinal);
        Assert.Contains($"Supported Scopes:{Environment.NewLine}  - openid{Environment.NewLine}  - profile", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportTheMissingSigningKeys_When_TheDiscoveredDocumentHasNone()
    {
        var body = await GetReportAsync(options => options.ConfigurationManager = Manager(new OpenIdConnectConfiguration()));

        Assert.Contains("? ERROR: No signing keys found!", body, StringComparison.Ordinal);
        Assert.Contains("  This will cause signature validation to fail.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ListEverySigningKey_When_TheDiscoveredDocumentHasKeys()
    {
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(new SymmetricSecurityKey(new byte[32]) { KeyId = "kid-1" });
        configuration.SigningKeys.Add(new SymmetricSecurityKey(new byte[32]));

        var body = await GetReportAsync(options => options.ConfigurationManager = Manager(configuration));

        Assert.Contains("? Signing Keys: 2 key(s) found", body, StringComparison.Ordinal);
        Assert.Contains("  - KeyId: kid-1", body, StringComparison.Ordinal);
        Assert.Contains("  - KeyId: N/A", body, StringComparison.Ordinal);
        Assert.Contains($"    Type: {nameof(SymmetricSecurityKey)}", body, StringComparison.Ordinal);
        Assert.Contains("    CryptoProviderFactory: CryptoProviderFactory", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_SkipTheDocumentSection_When_TheManagerReturnsNoConfiguration()
    {
        var body = await GetReportAsync(options => options.ConfigurationManager = new FixedConfigurationManager(() => null!));

        Assert.Contains("Attempting to retrieve OIDC configuration...", body, StringComparison.Ordinal);
        Assert.DoesNotContain("retrieved successfully", body, StringComparison.Ordinal);
        Assert.Contains("  ⚠ Could not retrieve OIDC configuration to discover endpoint", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportIntrospectionBypassed_When_IntrospectionIsDisabled()
    {
        var body = await GetReportAsync(configureBearer: null, new() { ["Idp:Introspection:Enabled"] = "false" });

        Assert.Contains("Enabled: ✗ NO (bypassed)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Authentication Mode:", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportAnonymousModeAndDiscovery_When_IntrospectionHasNoCredentialsAndNoEndpoint()
    {
        var body = await GetReportAsync(options => options.Authority = Authority);

        Assert.Contains("Enabled: ✓ YES", body, StringComparison.Ordinal);
        Assert.Contains("Authentication Mode: Anonymous", body, StringComparison.Ordinal);
        Assert.Contains("  Client ID: ✗ Not configured", body, StringComparison.Ordinal);
        Assert.Contains("  Client Secret: ✗ Not configured", body, StringComparison.Ordinal);
        Assert.Contains($"Endpoint: Will be discovered from {Authority}/.well-known/openid-configuration", body, StringComparison.Ordinal);
        Assert.Contains("Cache: Enabled (distributed memory cache)", body, StringComparison.Ordinal);
        Assert.Contains("  TTL Strategy: Based on token expiration (min 5s, max 5min, default 60s)", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportBasicAuthAndTheExplicitEndpoint_When_CredentialsAndEndpointAreConfigured()
    {
        var body = await GetReportAsync(configureBearer: null, new()
        {
            ["Idp:Introspection:Enabled"] = "true",
            ["Idp:Introspection:ClientId"] = "gateway",
            ["Idp:Introspection:ClientSecret"] = "secret-value",
            ["Idp:Introspection:Endpoint"] = "https://idp.diagnostics.example/explicit",
        });

        Assert.Contains("Authentication Mode: Basic Auth", body, StringComparison.Ordinal);
        Assert.Contains("  Client ID: ✓ Configured", body, StringComparison.Ordinal);
        Assert.Contains("  Client Secret: ✓ Configured", body, StringComparison.Ordinal);
        Assert.Contains("Endpoint: https://idp.diagnostics.example/explicit (explicit)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportAnonymousMode_When_OnlyTheClientIdIsConfigured()
    {
        var body = await GetReportAsync(configureBearer: null, new() { ["Idp:Introspection:ClientId"] = "gateway" });

        Assert.Contains("Authentication Mode: Anonymous", body, StringComparison.Ordinal);
        Assert.Contains("  Client ID: ✓ Configured", body, StringComparison.Ordinal);
        Assert.Contains("  Client Secret: ✗ Not configured", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportTheDiscoveredIntrospectionEndpoint_When_TheDocumentAdvertisesOne()
    {
        var configuration = new OpenIdConnectConfiguration();
        configuration.AdditionalData["introspection_endpoint"] = DiscoveredIntrospection;

        var body = await GetReportAsync(options => options.ConfigurationManager = Manager(configuration));

        Assert.Contains($"  Discovered Endpoint: {DiscoveredIntrospection}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_WarnThatTheDocumentHasNoIntrospectionEndpoint_When_TheDocumentAdvertisesNone()
    {
        var body = await GetReportAsync(options => options.ConfigurationManager = Manager(new OpenIdConnectConfiguration()));

        Assert.Contains("  ⚠ introspection_endpoint not found in discovery document", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportTheErrorAndLogIt_When_RetrievingTheConfigurationFailsWithAnHttpError()
    {
        using var logs = new CapturingLoggerProvider();
        var failure = new HttpRequestException("discovery unreachable");

        var body = await GetReportAsync(
            options => options.ConfigurationManager = new FixedConfigurationManager(() => throw failure),
            logs: logs);

        Assert.Contains("? ERROR retrieving OIDC configuration:", body, StringComparison.Ordinal);
        Assert.Contains($"  Exception Type: {nameof(HttpRequestException)}", body, StringComparison.Ordinal);
        Assert.Contains("  Message: discovery unreachable", body, StringComparison.Ordinal);
        Assert.Contains("  StackTrace: ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("  Inner Exception:", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TokenValidationParameters:", body, StringComparison.Ordinal);
        var error = Assert.Single(logs.From<Program>(), entry => entry.Level == LogLevel.Error);
        Assert.Equal("Failed to retrieve OIDC configuration for diagnostics", error.Message);
    }

    [Fact]
    public async Task Should_ReportTheInnerException_When_TheHttpErrorWrapsAnother()
    {
        var failure = new HttpRequestException("discovery unreachable", new TimeoutException("socket timed out"));

        var body = await GetReportAsync(options => options.ConfigurationManager = new FixedConfigurationManager(() => throw failure));

        Assert.Contains("  Inner Exception:", body, StringComparison.Ordinal);
        Assert.Contains($"    Type: {nameof(TimeoutException)}", body, StringComparison.Ordinal);
        Assert.Contains("    Message: socket timed out", body, StringComparison.Ordinal);
    }

    private static StaticConfigurationManager<OpenIdConnectConfiguration> Manager(OpenIdConnectConfiguration configuration) => new(configuration);

    private static async Task<string> GetReportAsync(
        Action<JwtBearerOptions>? configureBearer,
        Dictionary<string, string?>? settings = null,
        CapturingLoggerProvider? logs = null)
    {
        await using var app = await StartAsync(configureBearer, settings, logs);
        using var client = app.GetTestClient();
        return await client.GetStringAsync(Path, TestContext.Current.CancellationToken);
    }

    private static async Task<WebApplication> StartAsync(
        Action<JwtBearerOptions>? configureBearer,
        Dictionary<string, string?>? settings = null,
        CapturingLoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        if (logs is not null)
        {
            builder.Logging.AddProvider(logs);
        }

        if (configureBearer is not null)
        {
            builder.Services.Configure(Constants.BearerSchemeId, configureBearer);
        }

        var app = builder.Build();
        app.MapOidcDiagnostics();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private sealed class FixedConfigurationManager(Func<OpenIdConnectConfiguration> answer) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(answer());

        public void RequestRefresh()
        {
        }
    }
}
