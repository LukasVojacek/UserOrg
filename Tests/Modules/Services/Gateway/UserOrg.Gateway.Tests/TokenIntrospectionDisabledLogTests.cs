using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Authentication;
using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins the once-per-process line that says token introspection is switched off (#336): a Warning
/// outside Development, where it is a security-relevant setting, a Debug inside it, and written when
/// the host starts rather than at the first validated token.
/// </summary>
public sealed class TokenIntrospectionDisabledLogTests
{
    private const string DisabledLine = "[INTROSPECTION] Token introspection is DISABLED";
    private const string Authority = "https://idp.test/realms/master";
    private const int BearerAuthorityConfiguredEventId = 15;

    private static readonly Dictionary<string, string?> DisabledIntrospection = new()
    {
        ["Idp:Authority"] = Authority,
        ["Idp:Audience"] = "userorg",
        ["Idp:Introspection:Enabled"] = "false",
    };

    [Theory]
    [InlineData("Production", LogLevel.Warning)]
    [InlineData("Staging", LogLevel.Warning)]
    [InlineData("Development", LogLevel.Debug)]
    public void Should_LogTheDisabledLineAtTheEnvironmentsLevel_When_IntrospectionIsDisabled(string environment, LogLevel expected)
    {
        using var logs = new CapturingLoggerProvider();
        using var provider = IntrospectionServices(logs, environment);

        ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        Assert.Equal(expected, SingleDisabledLine(logs).Level);
    }

    [Fact]
    public async Task Should_LogTheDisabledLineAtHostStart_When_NoRequestHasArrived()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = BuildGateway(logs);

        await app.StartAsync(TestContext.Current.CancellationToken);

        SingleDisabledLine(logs);
    }

    [Fact]
    public async Task Should_LogTheBearerAuthorityOnceAtInformation_When_TheHostStarts()
    {
        using var logs = new CapturingLoggerProvider();
        await using var app = BuildGateway(logs);

        await app.StartAsync(TestContext.Current.CancellationToken);

        var configured = Assert.Single(logs.All, entry => entry.EventId.Id == BearerAuthorityConfiguredEventId && entry.Message.Contains(Authority, StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, configured.Level);
    }

    [Fact]
    public async Task Should_NotLogTheBypass_When_IntrospectionIsDisabledAndATokenIsIntrospected()
    {
        using var logs = new CapturingLoggerProvider();
        using var provider = IntrospectionServices(logs, Environments.Production);
        var introspection = ActivatorUtilities.CreateInstance<TokenIntrospectionService>(provider);

        await introspection.IntrospectAsync("opaque-token-1", TestContext.Current.CancellationToken);
        await introspection.IntrospectAsync("opaque-token-2", TestContext.Current.CancellationToken);

        var only = Assert.Single(logs.From<TokenIntrospectionService>());
        Assert.StartsWith(DisabledLine, only.Message, StringComparison.Ordinal);
    }

    private static CapturedLogEntry SingleDisabledLine(CapturingLoggerProvider logs)
        => Assert.Single(logs.From<TokenIntrospectionService>(), entry => entry.Message.StartsWith(DisabledLine, StringComparison.Ordinal));

    private static ServiceProvider IntrospectionServices(CapturingLoggerProvider logs, string environment)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddHttpClient();
        services.AddDistributedMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(DisabledIntrospection).Build());
        services.AddSingleton(Mock.Of<IHostEnvironment>(host => host.EnvironmentName == environment));
        return services.BuildServiceProvider();
    }

    private static WebApplication BuildGateway(CapturingLoggerProvider logs)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(logs).AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace);
        builder.Configuration.AddInMemoryCollection(DisabledIntrospection);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<RecordingAuditEventSink>();
        builder.Services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        builder.AddAuthenticationServices();
        return builder.Build();
    }
}
