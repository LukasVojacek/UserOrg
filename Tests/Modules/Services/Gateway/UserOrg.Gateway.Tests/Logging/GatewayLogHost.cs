using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using Serilog.Events;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Features.RouteExistence;

namespace UserOrg.Gateway.Tests.Logging;

internal sealed record GatewayLogOptions(bool Diagnostic = false, string? PathBase = null, bool S2sTokensAvailable = true, bool V1ApiEnabled = true, RouteSurface? RouteSurface = null, string? StrictJwtAudience = null);

internal sealed class GatewayLogHost : WebApplicationFactory<Program>
{
    public const string Authority = "https://idp.gateway-log-tests.example";
    public const string Audience = "userorg";
    public const string RoleManagementAccessToken = "rm-harness-access-token";
    public const string RequestIdHeader = "X-Harness-Request";
    public const string ErrorAfterResponseHeader = "X-Harness-Error";

    private static readonly SymmetricSecurityKey SigningKey = new("gateway-log-harness-signing-key-0123456789abcdef"u8.ToArray());

    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly string _workDirectory = Directory.CreateTempSubdirectory("userorg-gateway-log-").FullName;
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _pending = new();
    private readonly StubUpstream _upstream;
    private readonly GatewayLogOptions _options;

    private GatewayLogHost(StubUpstream upstream, GatewayLogOptions options)
    {
        _upstream = upstream;
        _options = options;
    }

    public IReadOnlyList<LogEvent> Events => [.. GatewayLogCaptureSinkExtensions.EventsOf(_runId)];

    public IReadOnlyList<LogEvent> RequestSummaries => [.. Events.Where(IsRequestSummary)];

    public IReadOnlyList<string> UpstreamRequests => _upstream.Requests;

    public StepUpLoggingController Controller => Services.GetRequiredService<StepUpLoggingController>();

    public static async Task<GatewayLogHost> StartAsync(GatewayLogOptions? options = null)
    {
        var upstream = await StubUpstream.StartAsync();
        var host = new GatewayLogHost(upstream, options ?? new GatewayLogOptions());
        _ = host.Server;
        if (host._options.Diagnostic)
        {
            Assert.Equal(LogEventLevel.Debug, host.Controller.LevelSwitch.MinimumLevel);
        }

        return host;
    }

    /// <summary>
    /// Mints a token the harness accepts as coming from its IdP. <paramref name="audiences"/> replaces the
    /// single <see cref="Audience"/> with an <c>aud</c> array when given.
    /// </summary>
    public static string MintIdpToken(IReadOnlyDictionary<string, object> claims, IReadOnlyCollection<string>? audiences = null)
    {
        var tokenClaims = claims.ToDictionary();
        if (audiences is not null)
        {
            tokenClaims[JwtRegisteredClaimNames.Aud] = audiences.ToArray();
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = audiences is null ? Audience : null,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = tokenClaims,
        });
    }

    public async Task<int> SendAsync(string pathAndQuery, IReadOnlyDictionary<string, string>? headers = null)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completed = _pending.GetOrAdd(requestId, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var queryStart = pathAndQuery.IndexOf('?', StringComparison.Ordinal);

        var context = await Server.SendAsync(request =>
        {
            request.Request.Method = HttpMethods.Get;
            request.Request.Scheme = "https";
            request.Request.Host = new HostString("gateway.log-tests.example");
            request.Request.Path = queryStart < 0 ? pathAndQuery : pathAndQuery[..queryStart];
            request.Request.QueryString = queryStart < 0 ? QueryString.Empty : new QueryString(pathAndQuery[queryStart..]);
            request.Request.Headers[RequestIdHeader] = requestId;
            foreach (var (name, value) in headers ?? ReadOnlyDictionary<string, string>.Empty)
            {
                request.Request.Headers[name] = value;
            }
        });

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        return context.Response.StatusCode;
    }

    public IReadOnlyList<LogEvent> EventsContaining(string marker) =>
        [.. Events.Where(logEvent => Render(logEvent).Contains(marker, StringComparison.OrdinalIgnoreCase))];

    public static bool IsRequestSummary(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("IsRequestSummary", out var value) && value is ScalarValue { Value: true };

    public static bool IsCompletionEvent(LogEvent logEvent) =>
        logEvent.MessageTemplate.Text.StartsWith("HTTP ", StringComparison.Ordinal)
        && logEvent.MessageTemplate.Text.Contains("responded", StringComparison.Ordinal);

    public static string? SourceContextOf(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string source } ? source : null;

    public static string? ScalarOf(LogEvent logEvent, string propertyName) =>
        logEvent.Properties.TryGetValue(propertyName, out var value) ? Unwrap(value) : null;

    public static string? HeaderOf(LogEvent logEvent, string headerName)
    {
        if (!logEvent.Properties.TryGetValue("Headers", out var headers))
        {
            return null;
        }

        return headers switch
        {
            DictionaryValue dictionary => dictionary.Elements
                .Where(element => string.Equals(element.Key.Value?.ToString(), headerName, StringComparison.OrdinalIgnoreCase))
                .Select(element => Unwrap(element.Value))
                .FirstOrDefault(),
            StructureValue structure => structure.Properties
                .Where(property => string.Equals(property.Name, headerName, StringComparison.OrdinalIgnoreCase))
                .Select(property => Unwrap(property.Value))
                .FirstOrDefault(),
            _ => null,
        };
    }

    public static string Render(LogEvent logEvent)
    {
        var text = new StringBuilder()
            .AppendLine(logEvent.MessageTemplate.Text)
            .AppendLine(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        foreach (var (name, value) in logEvent.Properties)
        {
            text.Append(name).Append('=').AppendLine(value.ToString());
        }

        if (logEvent.Exception is not null)
        {
            text.AppendLine(logEvent.Exception.ToString());
        }

        return text.ToString();
    }

    public static string Describe(IEnumerable<LogEvent> events) =>
        string.Join(Environment.NewLine, events.Select(logEvent => $"[{logEvent.Level}] {SourceContextOf(logEvent)}: {logEvent.RenderMessage(CultureInfo.InvariantCulture)}"));

    public void AssertNoEventCarries(string marker)
    {
        var leaks = EventsContaining(marker);
        Assert.True(
            leaks.Count == 0,
            $"'{marker}' reached {leaks.Count} captured event(s):{Environment.NewLine}{string.Join(Environment.NewLine, leaks.Select(leak => DescribeLeak(leak, marker)))}");
    }

    private static string DescribeLeak(LogEvent logEvent, string marker)
    {
        var carriers = logEvent.Properties
            .Where(property => property.Value.ToString().Contains(marker, StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Key);
        return $"[{logEvent.Level}] {SourceContextOf(logEvent)}: {logEvent.MessageTemplate.Text} (carried by: {string.Join(", ", carriers)})";
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _upstream.DisposeAsync();
        GatewayLogCaptureSinkExtensions.Forget(_runId);
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        foreach (var (key, value) in Settings())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(CaptureSinkSettings()));

        builder.ConfigureTestServices(services =>
        {
            foreach (var hostedService in services.Where(IsBackgroundService).ToList())
            {
                services.Remove(hostedService);
            }

            services.RemoveAll<IInternalTokenProvider>();
            services.AddSingleton<IInternalTokenProvider>(new StubInternalTokenProvider(_options.S2sTokensAvailable));

            if (_options.RouteSurface is { } surface)
            {
                services.RemoveAll<RouteSurfaceProvider>();
                services.AddSingleton(RouteSurfaceFixture.ProviderOf(surface));
            }

            services.AddHttpContextAccessor();
            services.RemoveAll<IAuditEventSink>();
            services.AddSingleton<RecordingAuditEventSink>();
            services.AddSingleton<IAuditEventSink>(provider => provider.GetRequiredService<RecordingAuditEventSink>());

            services.PostConfigure<JwtBearerOptions>(Constants.BearerSchemeId, options =>
            {
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration { Issuer = Authority });
                options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });

            services.AddSingleton<IStartupFilter>(new CompletionSignal(this));
        });
    }

    private static bool IsBackgroundService(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IHostedService)
        && descriptor.ImplementationType?.Name != "GenericWebHostService";

    private static string? Unwrap(LogEventPropertyValue value) =>
        value is ScalarValue scalar ? scalar.Value?.ToString() : value.ToString();

    private IEnumerable<KeyValuePair<string, string?>> Settings()
    {
        yield return new("ImmuDb:Username", "immudb");
        yield return new("ImmuDb:Password", "n0tInTheClearPw");
        yield return new("ImmuDb:Database", "auditdb");
        yield return new("EncryptedSpool:SpoolDirectory", Path.Combine(_workDirectory, "spool"));
        yield return new("EncryptedSpool:EndpointBaseUrl", "https://audit-proxy.gateway-log-tests.example");
        yield return new("OghamSealing:KeyEndpoint", "https://audit-proxy.gateway-log-tests.example/.well-known/ogham-sealing-keys.json");
        yield return new("OghamSealing:CacheDirectory", Path.Combine(_workDirectory, "keys"));
        yield return new("SerilogStepUp:EnableOtlpExporter", "false");
        yield return new("Idp:Authority", Authority);
        yield return new("Idp:Audience", Audience);
        yield return new("Idp:Introspection:Enabled", "false");
        yield return new("ConnectionStrings:ForgeDBContext", "Server=127.0.0.1,1;Database=forge;User Id=harness;Password=harness;TrustServerCertificate=true;Connect Timeout=1");
        yield return new("RoleManagement:AccessToken", RoleManagementAccessToken);
        yield return new("FeatureManagement:V1Api", _options.V1ApiEnabled.ToString(CultureInfo.InvariantCulture));
        yield return new("services:userorg:http:0", _upstream.Address);
        if (_options.Diagnostic)
        {
            yield return new("SerilogStepUp:Mode", "Diagnostic");
        }

        if (_options.PathBase is { } pathBase)
        {
            yield return new("Gateway:PathBase", pathBase);
        }

        if (_options.StrictJwtAudience is { } strictJwtAudience)
        {
            yield return new("FeatureManagement:StrictJwtAudience", strictJwtAudience);
        }
    }

    private IEnumerable<KeyValuePair<string, string?>> CaptureSinkSettings()
    {
        yield return new("Serilog:Using:0", typeof(GatewayLogHost).Assembly.GetName().Name);
        yield return new("Serilog:WriteTo:0:Name", nameof(GatewayLogCaptureSinkExtensions.GatewayLogCapture));
        yield return new("Serilog:WriteTo:0:Args:runId", _runId);
    }

    private void Complete(string requestId)
    {
        if (_pending.TryRemove(requestId, out var completed))
        {
            completed.TrySetResult();
        }
    }

    private sealed class CompletionSignal(GatewayLogHost host) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var requestId = context.Request.Headers[RequestIdHeader].ToString();
                context.Response.OnCompleted(() =>
                {
                    host.Complete(requestId);
                    return Task.CompletedTask;
                });
                await nextMiddleware(context);
                if (context.Request.Headers.ContainsKey(ErrorAfterResponseHeader))
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("UserOrg.Gateway.Tests.Harness")
                        .LogError("Harness error after {StatusCode}", context.Response.StatusCode);
                }
            });
            next(app);
        };
    }
}