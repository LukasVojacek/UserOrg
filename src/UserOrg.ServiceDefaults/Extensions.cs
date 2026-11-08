using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

using HashiCorpVault;

using Lukdrasil.StepUpLogging;
using Lukdrasil.StepUpLogging.Audit.EncryptedSpool;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ogham.Sealing;
using Ogham.Sealing.AspNetCore;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using TokenDelegation;

using UserOrg.ServiceDefaults.Audit;
using UserOrg.ServiceDefaults.Http;
using UserOrg.ServiceDefaults.Logging;

using Waldur.Sync.Configuration;
using Waldur.Sync.Worker;

namespace UserOrg.ServiceDefaults;

// Adds common .NET Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
    private const string UnconfiguredAuditProxyUrl = "https://audit-proxy.invalid";
    private static readonly TimeSpan SealingKeyRefreshInterval = TimeSpan.FromMinutes(1);

    // dotnet-getdocument starts a real host (GetDocument.Insider, via HostFactoryResolver) to
    // regenerate the OpenAPI documents, with no appsettings.json, so both packages' ValidateOnStart
    // would fail `dotnet build`. The placeholders below exist for that one host and no other: a host
    // that is going to serve traffic must fail to start on missing audit configuration rather than
    // seal into a temp directory nobody drains.
    private static bool IsOpenApiDocumentGeneration =>
        Assembly.GetEntryAssembly()?.GetName().Name is "GetDocument.Insider";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder, HealthStatus vaultFailureStatus = HealthStatus.Unhealthy) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddLoggingModes();
        builder.Services.AddOghamSealing(opts =>
        {
            if (IsOpenApiDocumentGeneration)
            {
                opts.KeyEndpoint = new Uri($"{UnconfiguredAuditProxyUrl}/.well-known/ogham-sealing-keys.json");
                opts.CacheDirectory = Path.Combine(Path.GetTempPath(), "userorg-ogham-sealing-keys-unconfigured");
            }

            // why: a reset key store loses audit records until the key is refreshed (ADR-0116); the package default is longer.
            opts.RefreshInterval = SealingKeyRefreshInterval;
            builder.Configuration.GetSection(SealingOptions.SectionName).Bind(opts);
        });
        builder.Services.AddSingleton(sp => ResolveImmuDbCredentials(sp.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton<IAuditPayloadEncryptor, AuditSqlSealer>();
        builder.AddEncryptedSpoolAuditSink(opts =>
        {
            if (IsOpenApiDocumentGeneration)
            {
                opts.SpoolDirectory = Path.Combine(Path.GetTempPath(), "userorg-audit-spool-unconfigured");
                opts.EndpointBaseUrl = UnconfiguredAuditProxyUrl;
            }

            // Both stay in the archive forever, so configuration has to be able to correct them.
            opts.ModuleName = builder.Environment.ApplicationName;
            opts.Version = GetInformationalVersion();
            opts.DeliveryTimeout = TimeSpan.FromSeconds(10);
            // why: three MaxDrainBackoff (5 min) cycles without a delivery mean the spool is stuck, not just retrying.
            opts.OldestRecordMaxAge = TimeSpan.FromMinutes(15);
            builder.Configuration.GetSection("EncryptedSpool").Bind(opts);

            // why: /health maps every check, so an Unhealthy producer would leave readiness while writes still spool;
            // a dead-letter record still turns the check Unhealthy.
            opts.UnreachableStatus = HealthStatus.Degraded;
            opts.OldestRecordStatus = HealthStatus.Degraded;
        });

        builder.AddDefaultHealthChecks(vaultFailureStatus);

        builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            });

            http.AddServiceDiscovery();
        });

        return builder;
    }

    private static void AddLoggingModes(this IHostApplicationBuilder builder)
    {
        builder.AddProductionLoggingDefaults();
        builder.AddFrameworkLevelOverrides();
        builder.AddStepUpLogging();
        builder.Services.PostConfigure<StepUpLoggingOptions>(LogRedactionDefaults.Apply);
    }

    // Resolved eagerly at host start through the sink. A blank value is as unusable as an absent
    // one, so both fail here, on the host that will serve; only the document-generation host gets
    // a stand-in.
    private static ImmuDbCredentials ResolveImmuDbCredentials(IConfiguration configuration)
    {
        var credentials = configuration.GetSection("ImmuDb").Get<ImmuDbCredentials>();

        if (credentials is { Username.Length: > 0, Password.Length: > 0, Database.Length: > 0 })
        {
            return credentials;
        }

        return IsOpenApiDocumentGeneration
            ? new ImmuDbCredentials("unset", "unset", "unset")
            : throw new InvalidOperationException(
                "ImmuDb:Username, ImmuDb:Password and ImmuDb:Database must all be set: the audit unsealing proxy replays this login on the producer's behalf, so without them no audit record ever reaches the archive.");
    }

    private static string GetInformationalVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var isWaldurEnabled = builder.Configuration.GetSection(WaldurSyncOptions.SectionName)
            .Get<WaldurSyncOptions>()?.EnabledInstances.Any() ?? false;
        var includeDbStatements = builder.Environment.IsDevelopment();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("UserOrg.HealthChecks")
                    .AddMeter("UserOrg.Core.Outbox")
                    .AddMeter(TokenDelegationTelemetry.MeterName)
                    .AddStepUpLoggingMeters();

                if (isWaldurEnabled)
                {
                    metrics.AddMeter(WaldurSyncDiagnostics.SourceName);
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource("UserOrg.Gateway.Auth")
                    .AddSource("UserOrg.API.Auth")
                    .AddSource("UserOrg.Core.Outbox")
                    .AddSource(TokenDelegationTelemetry.ActivitySourceName)
                    .AddHashiCorpVaultInstrumentation()
                    .AddSqlClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithSqlCommand = (activity, command) =>
                        {
                            if (command is not IDbCommand dbCommand)
                            {
                                return;
                            }

                            var operation = GetDatabaseOperation(dbCommand);
                            activity.DisplayName = operation;
                            activity.SetTag("db.operation", operation);
                            activity.SetTag("db.operation.name", operation);
                            activity.SetTag("db.query.summary", operation);

                            if (includeDbStatements)
                            {
                                activity.SetTag("db.statement", dbCommand.CommandText);
                                activity.SetTag("db.query.text", dbCommand.CommandText);
                            }
                        };
                    });

                if (isWaldurEnabled)
                {
                    tracing.AddSource(WaldurSyncDiagnostics.SourceName);
                }

                tracing.AddAspNetCoreInstrumentation(tracing =>
                    {
                        // Don't trace requests to the health endpoint to avoid filling the dashboard with noise
                        tracing.Filter = httpContext =>
                            !(httpContext.Request.Path.StartsWithSegments(HealthEndpointPath, StringComparison.OrdinalIgnoreCase)
                              || httpContext.Request.Path.StartsWithSegments(AlivenessEndpointPath, StringComparison.OrdinalIgnoreCase));
                        tracing.EnrichWithHttpRequest = static (activity, _) => RedactPublicTokenPath(activity, "url.path");
                    })
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation(static tracing =>
                        tracing.EnrichWithHttpRequestMessage = static (activity, _) => RedactPublicTokenPath(activity, "url.full"));
            });

        return builder;
    }

    private static void RedactPublicTokenPath(Activity activity, string tag)
    {
        if (activity.GetTagItem(tag) is string value)
        {
            var redacted = LogRedactionDefaults.PublicTokenPath().Replace(value, "[REDACTED]");
            if (!ReferenceEquals(redacted, value))
            {
                activity.SetTag(tag, redacted);
            }
        }
    }

    private static readonly char[] SqlCommandSplitChars = [' ', '\r', '\n', '\t', '('];

    private static string GetDatabaseOperation(IDbCommand command)
    {
        if (command.CommandType != CommandType.Text)
        {
            return command.CommandType.ToString();
        }

        if (string.IsNullOrWhiteSpace(command.CommandText))
        {
            return "SQL";
        }

        var parts = command.CommandText.TrimStart()
            .Split(SqlCommandSplitChars, 2, StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 0 ? "SQL" : parts[0].ToUpperInvariant();
    }

    /// <summary>
    /// Registers the baseline set of health checks split by liveness and readiness.
    /// <para>
    /// Tags and expected behaviour:
    /// <list type="bullet">
    ///   <item><term>live</term><description>Self check — always Healthy; drives <c>/alive</c> liveness probe.</description></item>
    ///   <item><term>db</term><description>Primary database connectivity via <c>SELECT 1</c> — Unhealthy on failure; drives <c>/health</c> readiness probe.</description></item>
    ///   <item><term>vault</term><description>HashiCorp Vault availability via <c>/v1/sys/health</c> — failure status is controlled by <paramref name="vaultFailureStatus"/> (default: Unhealthy); registered only when <c>Services:vault:http</c> is configured.</description></item>
    ///   <item><term>external, emails</term><description>Mail-sender service via <c>/health</c> — Degraded on failure (registered only when <c>MailConfiguration:MailSenderUrl</c> is configured).</description></item>
    ///   <item><term>system</term><description>Process allocated-memory threshold (<c>HealthChecks:MaximumAllocatedMemoryMb</c>, default 1024 MB). Degraded when exceeded.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="vaultFailureStatus">
    /// The <see cref="HealthStatus"/> reported when Vault is unreachable.
    /// Use <see cref="HealthStatus.Degraded"/> for services where Vault is not on the critical path
    /// (e.g. a gateway where most endpoints do not require Vault).
    /// Defaults to <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder, HealthStatus vaultFailureStatus = HealthStatus.Unhealthy) where TBuilder : IHostApplicationBuilder
    {
        var sqlConnectionString = builder.Configuration.GetConnectionString("UserOrgDBContext");
        var vaultBaseUrl = builder.Configuration["Services:vault:http"];
        var mailSenderUrl = builder.Configuration["MailConfiguration:MailSenderUrl"];
        var maximumAllocatedMemoryMb = builder.Configuration.GetValue("HealthChecks:MaximumAllocatedMemoryMb", 1024);

        var checks = builder.Services.AddHealthChecks()
            // Liveness: always Healthy — signals the process is alive
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            // Process memory threshold: Degraded above HealthChecks:MaximumAllocatedMemoryMb (default 1024 MB)
            .AddProcessAllocatedMemoryHealthCheck(
                maximumMegabytesAllocated: maximumAllocatedMemoryMb,
                name: "memory",
                failureStatus: HealthStatus.Degraded,
                tags: ["system"]);

        if (!string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            checks.AddSqlServer(
                connectionString: sqlConnectionString,
                healthQuery: "SELECT 1;",
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["db"],
                timeout: TimeSpan.FromSeconds(5));
        }

        if (!string.IsNullOrWhiteSpace(vaultBaseUrl))
        {
            checks.AddUrlGroup(
                new Uri($"{vaultBaseUrl.TrimEnd('/')}/v1/sys/health"),
                name: "vault",
                failureStatus: vaultFailureStatus,
                tags: ["vault"],
                timeout: TimeSpan.FromSeconds(5));
        }

        var isReadOnly = builder.Configuration.GetValue<bool>("FeatureManagement:ReadOnlyMode");
        if (!string.IsNullOrWhiteSpace(mailSenderUrl) && !isReadOnly)
        {
            checks.AddUrlGroup(
                new Uri($"{mailSenderUrl.TrimEnd('/')}/health"),
                name: "emailsender",
                failureStatus: HealthStatus.Degraded,
                tags: ["external", "emails"],
                timeout: TimeSpan.FromSeconds(5));
        }

        builder.Services.AddSingleton<IHealthCheckPublisher, OtelHealthCheckPublisher>();
        builder.Services.Configure<HealthCheckPublisherOptions>(opts =>
        {
            opts.Delay = TimeSpan.FromSeconds(5);
            opts.Period = TimeSpan.FromSeconds(60);
        });

        return builder;
    }

    /// <summary>
    /// Maps the <c>/health</c> readiness and <c>/alive</c> liveness endpoints.
    /// </summary>
    /// <param name="app">The web application to map endpoints on.</param>
    /// <param name="capAtDegradedTags">
    /// Optional set of health check tags whose <see cref="HealthStatus.Unhealthy"/> result should be
    /// treated as <see cref="HealthStatus.Degraded"/> when computing the <em>overall</em> status
    /// reported in the <c>/health</c> response. Individual check entries always reflect their true status.
    /// <para>
    /// Example: passing <c>["vault"]</c> means that if Vault is unreachable the overall status will be
    /// <c>Degraded</c> rather than <c>Unhealthy</c>, because most endpoints do not require Vault.
    /// </para>
    /// </param>
    public static WebApplication MapDefaultEndpoints(this WebApplication app, IReadOnlySet<string>? capAtDegradedTags = null)
    {
        // Readiness: all checks must pass; returns machine-readable JSON.
        // The response writer computes a capped overall status when capAtDegradedTags is provided.
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions
        {
            ResponseWriter = (ctx, report) => WriteHealthCheckJsonResponse(ctx, report, capAtDegradedTags)
        });

        // Liveness: only "live"-tagged checks must pass; returns plain-text status
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
            ResponseWriter = WriteAliveResponse
        });

        return app;
    }

    /// <summary>
    /// Computes the effective overall health status, optionally capping the contribution of
    /// checks whose tags overlap <paramref name="capAtDegradedTags"/> at
    /// <see cref="HealthStatus.Degraded"/> instead of <see cref="HealthStatus.Unhealthy"/>.
    /// </summary>
    private static HealthStatus ComputeOverallStatus(HealthReport report, IReadOnlySet<string>? capAtDegradedTags)
    {
        if (capAtDegradedTags is null || capAtDegradedTags.Count == 0)
        {
            return report.Status;
        }

        var overall = HealthStatus.Healthy;
        foreach (var entry in report.Entries.Values)
        {
            // Unhealthy entries whose tags overlap the cap set contribute only Degraded to the aggregate.
            var contribution = entry.Status == HealthStatus.Unhealthy && capAtDegradedTags.Overlaps(entry.Tags)
                ? HealthStatus.Degraded
                : entry.Status;

            if (contribution < overall)
            {
                overall = contribution;
            }
        }

        return overall;
    }

    private static readonly JsonSerializerOptions _healthJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static Task WriteHealthCheckJsonResponse(HttpContext context, HealthReport report, IReadOnlySet<string>? capAtDegradedTags = null)
    {
        var overallStatus = ComputeOverallStatus(report, capAtDegradedTags);

        // Override the HTTP status code that HealthCheckMiddleware already set based on report.Status.
        // This ensures callers see 503 only when the capped overall is truly Unhealthy.
        context.Response.StatusCode = overallStatus == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

        context.Response.ContentType = "application/json";

        var result = new
        {
            status = overallStatus.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    duration = e.Value.Duration.TotalMilliseconds
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(result, _healthJsonOptions));
    }

    private static Task WriteAliveResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync(report.Status.ToString());
    }
}