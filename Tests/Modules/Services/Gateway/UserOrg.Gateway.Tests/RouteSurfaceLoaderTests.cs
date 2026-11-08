using System.Net;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.RouteExistence;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the load-and-retry loop of <see cref="RouteSurfaceLoader"/>: an unreachable or slow
/// upstream is an Information wait and a retry, an unparseable document a Warning that publishes nothing,
/// an unchanged document set is not republished, a changed one is, and a disabled oracle never asks.
/// </summary>
public sealed class RouteSurfaceLoaderTests
{
    private const string ServedDocument = """{"paths":{"/api/v2/first":{"get":{}}}}""";
    private const string ChangedDocument = """{"paths":{"/api/v2/second":{"get":{}}}}""";
    private const string LoadedMessage = "Route surface loaded from the published OpenAPI documents.";

    [Fact]
    public async Task Should_LogAndNeverAsk_When_TheOracleIsDisabled()
    {
        using var logs = new CapturingLoggerProvider();
        using var handler = new ScriptedHandler((_, _) => Document(ServedDocument));
        var provider = new RouteSurfaceProvider();
        await using var container = BuildContainer(handler, provider, logs, ("RouteExistence:Enabled", "false"));
        var loader = Loader(container);

        await loader.StartAsync(TestContext.Current.CancellationToken);
        await loader.ExecuteTask!;

        Assert.Equal(0, handler.RequestCount);
        var disabled = Assert.Single(logs.From<RouteSurfaceLoader>());
        Assert.Equal(LogLevel.Information, disabled.Level);
        Assert.Equal("Route-existence oracle disabled; unmatched paths keep their pre-ADR-0106 answer.", disabled.Message);
        Assert.Same(RouteSurface.Empty, provider.Current);
    }

    [Fact]
    public async Task Should_LogAnInformationWaitAndRetry_When_TheUpstreamIsUnreachable()
    {
        using var logs = new CapturingLoggerProvider();
        using var handler = new ScriptedHandler((call, _) => call == 1 ? throw new HttpRequestException("connection refused") : Document(ServedDocument));
        var provider = new RouteSurfaceProvider();
        await using var container = BuildContainer(handler, provider, logs);
        var loader = Loader(container);

        await RunUntilAsync(loader, () => logs.From<RouteSurfaceLoader>().Any(entry => entry.Message == LoadedMessage));

        var wait = logs.From<RouteSurfaceLoader>()[0];
        Assert.Equal(LogLevel.Information, wait.Level);
        Assert.Matches(@"^Route surface not loaded yet: /api/[^/]+/openapi\.json is unavailable \(connection refused\); retrying\.$", wait.Message);
        Assert.Equal(ERouteExistence.Served, provider.Current.Evaluate(new Microsoft.AspNetCore.Http.PathString("/api/v2/first")));
    }

    [Fact]
    public async Task Should_LogATimeoutWait_When_TheRequestIsCancelledByItsTimeout()
    {
        using var logs = new CapturingLoggerProvider();
        using var handler = new ScriptedHandler((call, _) => call == 1 ? throw new TaskCanceledException() : Document(ServedDocument));
        var provider = new RouteSurfaceProvider();
        await using var container = BuildContainer(handler, provider, logs);
        var loader = Loader(container);

        await RunUntilAsync(loader, () => logs.From<RouteSurfaceLoader>().Any(entry => entry.Message == LoadedMessage));

        var wait = logs.From<RouteSurfaceLoader>()[0];
        Assert.Equal(LogLevel.Information, wait.Level);
        Assert.Matches(@"^Route surface not loaded yet: /api/[^/]+/openapi\.json is unavailable \(the request timed out\); retrying\.$", wait.Message);
    }

    [Fact]
    public async Task Should_LogAWarningAndPublishNothing_When_ADocumentCannotBeParsed()
    {
        using var logs = new CapturingLoggerProvider();
        using var handler = new ScriptedHandler((_, _) => Document("not json"));
        var provider = new RouteSurfaceProvider();
        await using var container = BuildContainer(handler, provider, logs);
        var loader = Loader(container);

        await RunUntilAsync(loader, () => logs.From<RouteSurfaceLoader>().Count(entry => entry.Level == LogLevel.Warning) >= 2);

        var warning = logs.From<RouteSurfaceLoader>()[0];
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("Route surface not loaded: an OpenAPI document could not be parsed; the gateway keeps its previous surface.", warning.Message);
        Assert.Same(RouteSurface.Empty, provider.Current);
        Assert.DoesNotContain(logs.From<RouteSurfaceLoader>(), entry => entry.Message == LoadedMessage);
    }

    [Fact]
    public async Task Should_KeepThePublishedSurface_When_TheDocumentsAreUnchangedOnRefresh()
    {
        using var logs = new CapturingLoggerProvider();
        using var handler = new ScriptedHandler((_, _) => Document(ServedDocument));
        var provider = new RouteSurfaceProvider();
        await using var container = BuildContainer(handler, provider, logs);
        var loader = Loader(container);
        RouteSurface? first = null;

        await RunUntilAsync(loader, () =>
        {
            first ??= ReferenceEquals(provider.Current, RouteSurface.Empty) ? null : provider.Current;
            return first is not null && handler.RequestCount >= handler.FirstRoundSize * 3;
        });

        Assert.Same(first, provider.Current);
        Assert.Single(logs.From<RouteSurfaceLoader>(), entry => entry.Message == LoadedMessage);
    }

    [Fact]
    public async Task Should_PublishTheNewSurface_When_TheDocumentsChangeOnRefresh()
    {
        using var logs = new CapturingLoggerProvider();
        using var handler = new ScriptedHandler((call, roundSize) => Document(call <= roundSize ? ServedDocument : ChangedDocument));
        var provider = new RouteSurfaceProvider();
        await using var container = BuildContainer(handler, provider, logs);
        var loader = Loader(container);

        await RunUntilAsync(loader, () => logs.From<RouteSurfaceLoader>().Count(entry => entry.Message == LoadedMessage) >= 2);

        Assert.Equal(ERouteExistence.Served, provider.Current.Evaluate(new Microsoft.AspNetCore.Http.PathString("/api/v2/second")));
        Assert.Equal(ERouteExistence.NotServed, provider.Current.Evaluate(new Microsoft.AspNetCore.Http.PathString("/api/v2/first")));
    }

    private static RouteSurfaceLoader Loader(ServiceProvider container) =>
        container.GetServices<IHostedService>().OfType<RouteSurfaceLoader>().Single();

    private static async Task RunUntilAsync(RouteSurfaceLoader loader, Func<bool> condition)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(30));
        await loader.StartAsync(cancellation.Token);
        try
        {
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
            }
        }
        finally
        {
            await loader.StopAsync(CancellationToken.None);
        }
    }

    private static HttpResponseMessage Document(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static ServiceProvider BuildContainer(HttpMessageHandler handler, RouteSurfaceProvider provider, ILoggerProvider logs, params (string Key, string Value)[] settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var configuration = new Dictionary<string, string?>
        {
            ["RouteExistence:RefreshInterval"] = "00:00:00.010",
            ["RouteExistence:RetryInterval"] = "00:00:00.010",
        };
        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Logging.AddProvider(logs);
        builder.AddRouteExistenceFeature();
        builder.Services.AddSingleton(provider);
        builder.Services.AddHttpClient(RouteSurfaceLoader.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return builder.Services.BuildServiceProvider();
    }

    /// <summary>
    /// Answers request number <c>call</c> (1-based) through <c>respond(call, roundSize)</c>, where
    /// <c>roundSize</c> is how many documents one complete load asks for.
    /// </summary>
    private sealed class ScriptedHandler(Func<int, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        public int FirstRoundSize { get; } = UserOrg.Shared.OpenApi.ApiDocuments.Published(true).Length;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _requestCount);
            return Task.FromResult(respond(call, FirstRoundSize));
        }
    }
}
