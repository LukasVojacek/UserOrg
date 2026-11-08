
using Lukdrasil.StepUpLogging;
using Lukdrasil.StepUpLogging.Audit.EncryptedSpool;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Ogham.Sealing;

using UserOrg.ServiceDefaults;
using UserOrg.ServiceDefaults.Audit;

namespace ServiceDefaults.Tests;

/// <summary>
/// Verifies that <see cref="Extensions.AddServiceDefaults{TBuilder}"/> wires the encrypted-spool
/// audit sink - <see cref="AuditSqlSealer"/> as the payload encryptor, not the retired
/// logging-only PoC - binds every audit option from configuration, and refuses to start a serving
/// host that has no audit configuration at all.
/// </summary>
public sealed class AuditSinkWiringTests
{
    private static readonly string SpoolDirectory = Path.Combine(Path.GetTempPath(), "userorg-audit-wiring-tests-spool");
    private static readonly string SealingKeyCacheDirectory = Path.Combine(Path.GetTempPath(), "userorg-audit-wiring-tests-keys");
    private const string EndpointBaseUrl = "https://audit-proxy.wiring-tests.example";
    private const string KeyEndpoint = "https://audit-proxy.wiring-tests.example/.well-known/ogham-sealing-keys.json";

    private static WebApplicationBuilder CreateBuilder(string applicationName, params KeyValuePair<string, string?>[] extraConfiguration)
    {
        var builder = CreateUnconfiguredBuilder(applicationName);

        builder.Configuration.AddInMemoryCollection([
            new("ImmuDb:Username", "immudb"),
            new("ImmuDb:Password", "n0tInTheClearPw"),
            new("ImmuDb:Database", "auditdb"),
            new("EncryptedSpool:SpoolDirectory", SpoolDirectory),
            new("EncryptedSpool:EndpointBaseUrl", EndpointBaseUrl),
            new("OghamSealing:KeyEndpoint", KeyEndpoint),
            new("OghamSealing:CacheDirectory", SealingKeyCacheDirectory)
        ]);
        builder.Configuration.AddInMemoryCollection(extraConfiguration);

        return builder;
    }

    private static WebApplicationBuilder CreateUnconfiguredBuilder(string applicationName) =>
        WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = applicationName
        });

    [Fact(DisplayName = "Should_RegisterAuditSqlSealer_As_ThePayloadEncryptor")]
    public void Should_RegisterAuditSqlSealer_As_ThePayloadEncryptor()
    {
        var builder = CreateBuilder("UserOrg.Api");

        builder.AddServiceDefaults();

        var encryptor = Assert.Single(builder.Services, d => d.ServiceType == typeof(IAuditPayloadEncryptor));
        Assert.Equal(typeof(AuditSqlSealer), encryptor.ImplementationType);
    }

    [Fact(DisplayName = "Should_RegisterTheSinkFromThePackage_Not_TheRetiredLoggingSink")]
    public void Should_RegisterTheSinkFromThePackage_Not_TheRetiredLoggingSink()
    {
        var builder = CreateBuilder("UserOrg.Api");

        builder.AddServiceDefaults();

        var sink = Assert.Single(builder.Services, d => d.ServiceType == typeof(IAuditEventSink));
        Assert.Equal(typeof(EncryptedSpoolOptions).Assembly, sink.ImplementationType?.Assembly);
        Assert.NotEqual(typeof(Extensions).Assembly, sink.ImplementationType?.Assembly);
    }

    [Fact(DisplayName = "Should_BindTheSpoolOptions_From_Configuration")]
    public async Task Should_BindTheSpoolOptions_From_Configuration()
    {
        var builder = CreateBuilder("UserOrg.Api");
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value;
        Assert.Equal(SpoolDirectory, options.SpoolDirectory);
        Assert.Equal(EndpointBaseUrl, options.EndpointBaseUrl);
    }

    [Fact(DisplayName = "Should_BindTheSealingOptions_From_Configuration")]
    public async Task Should_BindTheSealingOptions_From_Configuration()
    {
        var builder = CreateBuilder("UserOrg.Api");
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<SealingOptions>>().Value;
        Assert.Equal(new Uri(KeyEndpoint), options.KeyEndpoint);
        Assert.Equal(SealingKeyCacheDirectory, options.CacheDirectory);
    }

    [Fact(DisplayName = "Should_BoundTheSealingKeyRefreshToOneMinute_When_ConfigurationIsSilent")]
    public async Task Should_BoundTheSealingKeyRefreshToOneMinute_When_ConfigurationIsSilent()
    {
        var builder = CreateBuilder("UserOrg.Api");
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<SealingOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(1), options.RefreshInterval);
    }

    [Fact(DisplayName = "Should_LetConfigurationOverrideTheSealingKeyRefresh_When_RefreshIntervalIsConfigured")]
    public async Task Should_LetConfigurationOverrideTheSealingKeyRefresh_When_RefreshIntervalIsConfigured()
    {
        var builder = CreateBuilder("UserOrg.Api", new KeyValuePair<string, string?>("OghamSealing:RefreshInterval", "00:00:30"));
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<SealingOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshInterval);
    }

    [Fact(DisplayName = "Should_UseDistinctModuleNames_When_TwoHostsHaveDifferentApplicationNames")]
    public async Task Should_UseDistinctModuleNames_When_TwoHostsHaveDifferentApplicationNames()
    {
        var apiBuilder = CreateBuilder("UserOrg.Api");
        var gatewayBuilder = CreateBuilder("UserOrg.Gateway");
        apiBuilder.AddServiceDefaults();
        gatewayBuilder.AddServiceDefaults();

        await using var api = apiBuilder.Build();
        await using var gateway = gatewayBuilder.Build();

        var apiModuleName = api.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value.ModuleName;
        var gatewayModuleName = gateway.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value.ModuleName;

        Assert.Equal("UserOrg.Api", apiModuleName);
        Assert.Equal("UserOrg.Gateway", gatewayModuleName);
    }

    [Fact(DisplayName = "Should_PreferTheConfiguredModuleName_Over_TheApplicationName")]
    public async Task Should_PreferTheConfiguredModuleName_Over_TheApplicationName()
    {
        var builder = CreateBuilder("UserOrg.Api", new KeyValuePair<string, string?>("EncryptedSpool:ModuleName", "UserOrg.Api.Legacy"));
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value;
        Assert.Equal("UserOrg.Api.Legacy", options.ModuleName);
    }

    [Fact(DisplayName = "Should_ReportAnUnreachableProxyAsDegraded_Even_When_ConfigurationSaysUnhealthy")]
    public async Task Should_ReportAnUnreachableProxyAsDegraded_Even_When_ConfigurationSaysUnhealthy()
    {
        var builder = CreateBuilder("UserOrg.Api", new KeyValuePair<string, string?>("EncryptedSpool:UnreachableStatus", "Unhealthy"));
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value;
        Assert.Equal(HealthStatus.Degraded, options.UnreachableStatus);
    }

    [Fact(DisplayName = "Should_ReportAStuckSpoolAfterFifteenMinutes_By_Default")]
    public async Task Should_ReportAStuckSpoolAfterFifteenMinutes_By_Default()
    {
        var builder = CreateBuilder("UserOrg.Api");
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(15), options.OldestRecordMaxAge);
    }

    [Fact(DisplayName = "Should_PreferTheConfiguredOldestRecordMaxAge")]
    public async Task Should_PreferTheConfiguredOldestRecordMaxAge()
    {
        var builder = CreateBuilder("UserOrg.Api", new KeyValuePair<string, string?>("EncryptedSpool:OldestRecordMaxAge", "00:30:00"));
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(30), options.OldestRecordMaxAge);
    }

    [Fact(DisplayName = "Should_ReportAStaleOldestRecordAsDegraded_Even_When_ConfigurationSaysUnhealthy")]
    public async Task Should_ReportAStaleOldestRecordAsDegraded_Even_When_ConfigurationSaysUnhealthy()
    {
        var builder = CreateBuilder("UserOrg.Api", new KeyValuePair<string, string?>("EncryptedSpool:OldestRecordStatus", "Unhealthy"));
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value;
        Assert.Equal(HealthStatus.Degraded, options.OldestRecordStatus);
    }

    [Fact(DisplayName = "Should_RefuseToStart_When_TheAuditConfigurationIsMissing")]
    public async Task Should_RefuseToStart_When_TheAuditConfigurationIsMissing()
    {
        var builder = CreateUnconfiguredBuilder("UserOrg.Api");
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var failure = Assert.Throws<OptionsValidationException>(
            () => host.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value);
        Assert.Contains(nameof(EncryptedSpoolOptions.SpoolDirectory), string.Join(' ', failure.Failures), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should_RefuseToStart_When_TheImmuDbCredentialsAreMissing")]
    public async Task Should_RefuseToStart_When_TheImmuDbCredentialsAreMissing()
    {
        var builder = CreateUnconfiguredBuilder("UserOrg.Api");
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        var failure = Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<ImmuDbCredentials>());
        Assert.Contains("ImmuDb:Password", failure.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should_RefuseToStart_When_TheImmuDbPasswordIsBlank")]
    public async Task Should_RefuseToStart_When_TheImmuDbPasswordIsBlank()
    {
        var builder = CreateBuilder("UserOrg.Api", new KeyValuePair<string, string?>("ImmuDb:Password", string.Empty));
        builder.AddServiceDefaults();

        await using var host = builder.Build();

        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<ImmuDbCredentials>());
    }
}
