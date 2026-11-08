using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using OpenTelemetry;
using OpenTelemetry.Trace;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

internal sealed record CapturedActivity(string SourceName, IReadOnlyDictionary<string, string?> Tags);

internal sealed class RequestPipelineHost : IAsyncDisposable
{
    public const string PathBase = "/api/userorg";
    public const string BodyProbePath = "/body-probe";
    public const string DownstreamClientName = "token-path-downstream";
    public const string DownstreamOrigin = "http://downstream.test";
    public const string ServerSource = "Microsoft.AspNetCore";
    public const string HttpClientSource = "System.Net.Http";

    private static readonly string ApplicationName = typeof(RequestPipelineHost).Assembly.GetName().Name!;

    private static readonly string[] TokenRoutes =
    [
        "/api/v2/Public/ActionLinks/{token}",
        "/api/v2/Public/Requests/ByLink/{actionLink}",
        "/api/v2/Public/ResourceRequestActions/{token}",
        "/api/Public/Handle/{actionLink}",
        "/api/Action/Handle/{token}"
    ];

    private readonly LoggingPipelineHost _logging;
    private readonly ConcurrentQueue<CapturedActivity> _activities;
    private readonly HttpClient _client;

    private RequestPipelineHost(LoggingPipelineHost logging, ConcurrentQueue<CapturedActivity> activities)
    {
        _logging = logging;
        _activities = activities;
        _client = logging.App.GetTestClient();
    }

    public IReadOnlyList<LogEvent> Events => _logging.Events;

    public StepUpLoggingController Controller => _logging.Controller;

    public IReadOnlyList<CapturedActivity> Activities => [.. _activities];

    public static async Task<RequestPipelineHost> StartAsync(IEnumerable<KeyValuePair<string, string?>>? settings = null)
    {
        var activities = new ConcurrentQueue<CapturedActivity>();
        var logging = LoggingPipelineHost.Build(settings, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.RemoveAll<IHostedService>();
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new ActivityCapture(activities)));
            builder.Services.AddHttpClient(DownstreamClientName)
                .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
                {
                    ConnectCallback = static (_, _) => ValueTask.FromResult<Stream>(new NoContentResponseStream())
                });
        }, ApplicationName);

        try
        {
            var app = logging.App;
            app.UsePathBase(PathBase);
            app.UseRouting();
            app.UseStepUpRequestLogging();
            foreach (var route in TokenRoutes)
            {
                app.MapGet(route, NoContent);
            }

            app.MapPost(BodyProbePath, NoContent);
            app.Map("/{**rest}", NoContent);

            _ = app.Services.GetRequiredService<TracerProvider>();
            await app.StartAsync();
            return new RequestPipelineHost(logging, activities);
        }
        catch
        {
            await logging.DisposeAsync();
            throw;
        }
    }

    public async Task<HttpResponseMessage> GetAsync(string pathAndQuery) =>
        await _client.GetAsync(new Uri(pathAndQuery, UriKind.Relative), TestContext.Current.CancellationToken);

    public async Task GetEachAsync(params IEnumerable<string> pathsAndQueries)
    {
        foreach (var pathAndQuery in pathsAndQueries)
        {
            using var response = await GetAsync(pathAndQuery);
        }
    }

    public async Task<HttpResponseMessage> PostAsync(string path, HttpContent content) =>
        await _client.PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

    public async Task<CapturedActivity> ServerSpanOfAsync(string pathAndQuery)
    {
        var before = SpansFrom(ServerSource).Count;
        using var response = await GetAsync(pathAndQuery);
        return await NextSpanAsync(ServerSource, before);
    }

    public async Task<CapturedActivity> HttpClientSpanOfAsync(string pathAndQuery)
    {
        var before = SpansFrom(HttpClientSource).Count;
        using var client = _logging.App.Services.GetRequiredService<IHttpClientFactory>().CreateClient(DownstreamClientName);
        using var response = await client.GetAsync(new Uri(DownstreamOrigin + pathAndQuery), TestContext.Current.CancellationToken);
        return await NextSpanAsync(HttpClientSource, before);
    }

    public IReadOnlyList<LogEvent> SummariesFor(string path) =>
        [.. Events.Where(e => IsRequestSummary(e)
            && e.Properties.TryGetValue("Path", out var value)
            && value is ScalarValue { Value: string summaryPath }
            && string.Equals(summaryPath, path, StringComparison.Ordinal))];

    public async Task<IReadOnlyList<LogEvent>> AwaitSummariesForAsync(string path)
    {
        await LoggingPipelineHost.WaitUntilAsync(() => SummariesFor(path).Count > 0);
        return SummariesFor(path);
    }

    public static bool IsRequestSummary(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("IsRequestSummary", out var value) && value is ScalarValue { Value: true };

    public static string TextOf(LogEvent logEvent) =>
        string.Join('\n', [
            logEvent.RenderMessage(CultureInfo.InvariantCulture),
            logEvent.Exception?.ToString() ?? string.Empty,
            .. logEvent.Properties.Values.SelectMany(ValuesOf)
        ]);

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _logging.App.StopAsync();
        await _logging.DisposeAsync();
    }

    private static IEnumerable<string> ValuesOf(LogEventPropertyValue value) => value switch
    {
        ScalarValue scalar => [Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty],
        SequenceValue sequence => sequence.Elements.SelectMany(ValuesOf),
        StructureValue structure => structure.Properties.SelectMany(property => ValuesOf(property.Value)),
        DictionaryValue dictionary => dictionary.Elements.SelectMany(entry => ValuesOf(entry.Key).Concat(ValuesOf(entry.Value))),
        _ => [value.ToString()]
    };

    private List<CapturedActivity> SpansFrom(string sourceName) =>
        [.. _activities.Where(activity => activity.SourceName == sourceName)];

    private async Task<CapturedActivity> NextSpanAsync(string sourceName, int before)
    {
        await LoggingPipelineHost.WaitUntilAsync(() => SpansFrom(sourceName).Count > before);
        var spans = SpansFrom(sourceName);
        Assert.True(spans.Count > before, $"no {sourceName} activity ended for the request");
        return spans[before];
    }

    private static IResult NoContent() => Results.NoContent();

    private sealed class ActivityCapture(ConcurrentQueue<CapturedActivity> activities) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            if (data.Source.Name is not (ServerSource or HttpClientSource))
            {
                return;
            }

            var tags = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (key, value) in data.TagObjects)
            {
                tags[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            activities.Enqueue(new CapturedActivity(data.Source.Name, tags));
        }
    }

    private sealed class NoContentResponseStream : Stream
    {
        private readonly MemoryStream _response = new("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"u8.ToArray());

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _response.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_response.Read(buffer.Span));

        public override void Write(byte[] buffer, int offset, int count)
        {
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}