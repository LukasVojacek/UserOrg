using System.Collections.Concurrent;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// Classes in this collection set process-wide environment variables (<c>LoggingEnvironmentOverrideTests</c>)
/// that every logging pipeline built in the process reads as configuration, so no pipeline may be built while
/// another class's variables are set. Every class that builds one runs in this collection, alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LoggingPipelineCollection
{
    public const string Name = "Logging pipeline";
}

/// <summary>
/// A host built through <see cref="Extensions.AddServiceDefaults{TBuilder}"/> from an empty content root, with
/// one config-declared Serilog WriteTo sink that records every event it is handed. StepUpLogging attaches that
/// sink to both the gated and the bypass logger, so an event exported twice is recorded twice.
/// </summary>
internal sealed class LoggingPipelineHost : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<LogEvent>> Captures = new();

    private readonly string _key;
    private readonly string _contentRoot;

    private LoggingPipelineHost(WebApplication app, string key, string contentRoot)
    {
        App = app;
        _key = key;
        _contentRoot = contentRoot;
        LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    }

    public WebApplication App { get; }

    public ILoggerFactory LoggerFactory { get; }

    public StepUpLoggingController Controller => App.Services.GetRequiredService<StepUpLoggingController>();

    public StepUpLoggingOptions StepUpOptions => App.Services.GetRequiredService<IOptions<StepUpLoggingOptions>>().Value;

    public IReadOnlyList<LogEvent> Events => [.. Captures[_key]];

    public static WebApplicationBuilder CreateBuilder(
        string applicationName,
        IEnumerable<KeyValuePair<string, string?>>? settings,
        out string key,
        out string contentRoot,
        string environmentName = "Production")
    {
        key = Guid.NewGuid().ToString("N");
        contentRoot = Directory.CreateTempSubdirectory("userorg-logging-pipeline-").FullName;
        Captures[key] = new ConcurrentQueue<LogEvent>();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            ApplicationName = applicationName,
            ContentRootPath = contentRoot
        });

        builder.Configuration.AddInMemoryCollection([
            new("ImmuDb:Username", "immudb"),
            new("ImmuDb:Password", "n0tInTheClearPw"),
            new("ImmuDb:Database", "auditdb"),
            new("EncryptedSpool:SpoolDirectory", Path.Combine(contentRoot, "spool")),
            new("EncryptedSpool:EndpointBaseUrl", "https://audit-proxy.logging-tests.example"),
            new("OghamSealing:KeyEndpoint", "https://audit-proxy.logging-tests.example/.well-known/ogham-sealing-keys.json"),
            new("OghamSealing:CacheDirectory", Path.Combine(contentRoot, "keys")),
            new("SerilogStepUp:EnableOtlpExporter", "false"),
            new("Serilog:Using:0", typeof(LoggingPipelineHost).Assembly.GetName().Name),
            new("Serilog:WriteTo:0:Name", nameof(CaptureSinkExtensions.LoggingPipelineCapture)),
            new("Serilog:WriteTo:0:Args:key", key)
        ]);

        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        return builder;
    }

    public static LoggingPipelineHost Build(
        IEnumerable<KeyValuePair<string, string?>>? settings = null,
        Action<WebApplicationBuilder>? afterServiceDefaults = null,
        string applicationName = "UserOrg.Api",
        Action<WebApplicationBuilder>? beforeServiceDefaults = null)
    {
        var builder = CreateBuilder(applicationName, settings, out var key, out var contentRoot);
        beforeServiceDefaults?.Invoke(builder);
        builder.AddServiceDefaults();
        afterServiceDefaults?.Invoke(builder);

        return new LoggingPipelineHost(builder.Build(), key, contentRoot);
    }

    public static IEnumerable<KeyValuePair<string, string?>> Diagnostic(int? durationMinutes = null)
    {
        yield return new("SerilogStepUp:Mode", "Diagnostic");
        if (durationMinutes is { } minutes)
        {
            yield return new("SerilogStepUp:DiagnosticDurationMinutes", minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    public string Log(string category, LogLevel level, string? message = null)
    {
        var body = message ?? $"probe {Guid.NewGuid():N}";
        LoggerFactory.CreateLogger(category).Log(level, "{Body}", body);
        return body;
    }

    public int CountOf(string body) => Events.Count(e => BodyOf(e) == body);

    public IReadOnlyList<LogEvent> From(string sourceContextSuffix) =>
        [.. Events.Where(e => e.Properties.TryGetValue("SourceContext", out var value)
            && value is ScalarValue { Value: string source }
            && source.EndsWith(sourceContextSuffix, StringComparison.Ordinal))];

    public static string? BodyOf(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("Body", out var value) && value is ScalarValue { Value: string body } ? body : null;

    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        Captures.TryRemove(_key, out _);
        try
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal static void Record(string key, LogEvent logEvent)
    {
        if (Captures.TryGetValue(key, out var queue))
        {
            queue.Enqueue(logEvent);
        }
    }
}

public static class CaptureSinkExtensions
{
    public static LoggerConfiguration LoggingPipelineCapture(this LoggerSinkConfiguration sinkConfiguration, string key) =>
        sinkConfiguration.Sink(new CaptureSink(key));

    private sealed class CaptureSink(string key) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => LoggingPipelineHost.Record(key, logEvent);
    }
}