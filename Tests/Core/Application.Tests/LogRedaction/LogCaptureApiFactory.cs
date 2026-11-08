using System.Net;
using System.Security.Claims;
using System.Text;

using Core.DataAccess.Queries.Contracts;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using Moq;

using Serilog.Events;

using UserOrg.Shared;
using UserOrg.Shared.FeatureManagement;

using UserOrganizationManagement.Application.Tests.Logging;
using UserOrganizationManagement.Application.Tests.TrustedEdge;
using UserOrganizationManagement.Application.UserOrg.IServices;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

internal sealed class LogCaptureApiFactory : WebApplicationFactory<Program>
{
    public const string GatewayAuthority = "https://gateway-log-redaction.invalid";
    public const string Subject = "5d0c3a8e-7b41-4c2a-9e55-0f1d2c3b4a51";

    private const string SinkKey = "LogRedactionCapture";
    private const string SentinelCategory = "UserOrganizationManagement.Application.Tests.LogRedaction.Sentinel";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("log-redaction-boot-test-signing-key-0123456789abcdef"));

    private static readonly SymmetricSecurityKey ForeignKey =
        new(Encoding.UTF8.GetBytes("log-redaction-foreign-signing-key-0123456789abcdef"));

    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly bool _diagnostic;
    private readonly Action<IServiceCollection>? _replaceServices;

    private LogCaptureApiFactory(bool diagnostic, Action<IServiceCollection>? replaceServices)
    {
        _diagnostic = diagnostic;
        _replaceServices = replaceServices;
    }

    public IReadOnlyList<LogEvent> Events => [.. ProbeCaptureSinkExtensions.EventsOf(_runId)];

    public static LogCaptureApiFactory Start(bool diagnosticMode = false, Action<IServiceCollection>? replaceServices = null)
    {
        var factory = new LogCaptureApiFactory(diagnosticMode, replaceServices);
        var environment = factory.HostEnvironment();
        var previous = environment.Select(pair => (pair.Key, Value: Environment.GetEnvironmentVariable(pair.Key))).ToList();
        try
        {
            foreach (var (key, value) in environment)
            {
                Environment.SetEnvironmentVariable(key, value);
            }

            _ = factory.Server;
            if (diagnosticMode)
            {
                Assert.Equal(LogEventLevel.Debug, factory.Services.GetRequiredService<StepUpLoggingController>().LevelSwitch.MinimumLevel);
            }
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        return factory;
    }

    public static string MintGatewayToken(string email) => Mint(SigningKey, email);

    public static string MintForeignToken() => Mint(ForeignKey, "foreign-signer@example.org");

    public Task<HttpContext> SendAsync(string method, string path, string? bearer = null) =>
        Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Method = method;
            context.Request.Path = path;
            if (bearer is not null)
            {
                context.Request.Headers.Authorization = $"Bearer {bearer}";
            }
        });

    public async Task SendSentinelAndAwaitItsSummaryAsync()
    {
        var sentinel = $"/api/log-redaction-sentinel/{Guid.NewGuid():N}";
        await SendAsync(HttpMethods.Get, sentinel);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!HasSummaryFor(sentinel) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(HasSummaryFor(sentinel), $"the sentinel request {sentinel} left no request summary in the capture sink");

        var marker = $"log-redaction-marker-{Guid.NewGuid():N}";
        Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
            Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger(SentinelCategory),
            "Log capture marker {Marker}",
            marker);

        while (!HasMarker(marker) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(HasMarker(marker), $"the regular logging pipeline did not deliver the marker {marker} to the capture sink; its earlier events cannot be trusted either");
    }

    private bool HasMarker(string marker) => Events.Any(logEvent => !logEvent.IsRequestSummary() && logEvent.Mentions(marker));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"FeatureManagement:{FeatureFlags.V1Api}", "true");
        builder.UseSetting($"FeatureManagement:{FeatureFlags.V2Api}", "true");
        builder.UseSetting("FeatureManagement:ReadOnlyMode", "true");

        builder.ConfigureTestServices(services =>
        {
            ProgramBootEnvironment.RemoveHostedServices(services);

            services.RemoveAll<IAuditEventSink>();
            services.AddSingleton<IAuditEventSink>(new RecordingAuditEventSink());

            ReplaceWithEmptyMock<IRequestQuery>(services);
            ReplaceWithEmptyMock<IProjectResourceRequestQuery>(services);
            ReplaceWithEmptyMock<IActionLinkService>(services);
            ReplaceWithEmptyMock<IProjectRequestService>(services);

            GatewayTestJwt.Accept(services, SigningKey, GatewayAuthority);

            _replaceServices?.Invoke(services);
        });
    }

    private static void ReplaceWithEmptyMock<TService>(IServiceCollection services)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddSingleton(new Mock<TService>().Object);
    }

    private static string Mint(SymmetricSecurityKey key, string email) =>
        GatewayTestJwt.Mint(key, GatewayAuthority, new()
        {
            [JwtRegisteredClaimNames.Sub] = Subject,
            [JwtRegisteredClaimNames.Email] = email,
            [ClaimTypes.Role] = RoleInfoConstants.PlatformUserRoleName,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
        });

    private bool HasSummaryFor(string path) => Events.Any(logEvent => logEvent.IsRequestSummary() && logEvent.Mentions(path));

    private KeyValuePair<string, string?>[] HostEnvironment() =>
    [
        .. ProgramBootEnvironment.Values("log-redaction-tests", "log-redaction-tests", GatewayAuthority),
        new("SerilogStepUp__EnableOtlpExporter", "false"),
        new("SerilogStepUp__Mode", _diagnostic ? "Diagnostic" : null),
        new($"Serilog__Using__{SinkKey}", typeof(ProbeCaptureSinkExtensions).Assembly.GetName().Name),
        new($"Serilog__WriteTo__{SinkKey}__Name", nameof(ProbeCaptureSinkExtensions.ProbeCapture)),
        new($"Serilog__WriteTo__{SinkKey}__Args__runId", _runId),
    ];
}