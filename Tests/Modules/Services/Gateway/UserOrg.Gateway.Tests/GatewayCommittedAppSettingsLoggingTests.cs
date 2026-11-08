using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using UserOrg.ServiceDefaults;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins the StepUpLogging options the Gateway resolves from its own committed appsettings.json: the file
/// must leave the production logging defaults intact, because configuration arrays merge by index and a
/// shorter ExcludePaths would silently drop entries.
/// </summary>
public sealed class GatewayCommittedAppSettingsLoggingTests
{
    private static readonly string[] ProductionExcludePaths =
    [
        "/health",
        "/alive",
        "/metrics",
        "/api/scalar.js",
        "/api/scalar.aspnetcore.js",
    ];

    private static readonly Dictionary<string, string?> AuditConfiguration = new()
    {
        ["ImmuDb:Username"] = "immudb",
        ["ImmuDb:Password"] = "n0tInTheClearPw",
        ["ImmuDb:Database"] = "auditdb",
        ["EncryptedSpool:SpoolDirectory"] = Path.Combine(Path.GetTempPath(), "userorg-gateway-logging-tests-spool"),
        ["EncryptedSpool:EndpointBaseUrl"] = "https://audit-proxy.gateway-logging-tests.example",
        ["OghamSealing:KeyEndpoint"] = "https://audit-proxy.gateway-logging-tests.example/.well-known/ogham-sealing-keys.json",
        ["OghamSealing:CacheDirectory"] = Path.Combine(Path.GetTempPath(), "userorg-gateway-logging-tests-keys"),
    };

    [Fact]
    public async Task Should_ResolveTheProductionLoggingDefaults_When_TheCommittedAppSettingsAreLoaded()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = GatewayContentRoot(),
            EnvironmentName = Environments.Production,
            ApplicationName = "UserOrg.Gateway",
        });
        builder.Configuration.AddInMemoryCollection(AuditConfiguration);
        builder.AddServiceDefaults();
        await using var app = builder.Build();

        var options = app.Services.GetRequiredService<IOptions<StepUpLoggingOptions>>().Value;

        Assert.Equal("RoleManagementAuth", app.Configuration["RoleManagement:CookieName"]);
        Assert.Equal("Warning", options.BaseLevel);
        Assert.Equal("Information", options.StepUpLevel);
        Assert.Equal(60, options.DurationSeconds);
        Assert.False(options.CaptureRequestBody);
        Assert.True(options.EnablePreErrorBuffering);
        Assert.Equal(50, options.PreErrorBufferSize);
        Assert.Equal(256, options.PreErrorMaxContexts);
        Assert.Equal(ProductionExcludePaths, options.ExcludePaths);
    }

    private static string GatewayContentRoot() =>
        Path.Combine(RepoRoot.Directory(), "src", "Modules", "Services", "Gateway", "UserOrg.Gateway");
}