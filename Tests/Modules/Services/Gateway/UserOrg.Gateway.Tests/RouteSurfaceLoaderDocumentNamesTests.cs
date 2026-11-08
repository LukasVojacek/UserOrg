using System.Collections.Concurrent;
using System.Net;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Features.RouteExistence;
using UserOrg.Shared.OpenApi;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The gateway half of the binding ADR-0106 depends on: the loader fetches one document per name in
/// <see cref="ApiDocuments.Published"/> for the <c>FeatureManagement:V1Api</c> value the gateway reads, the
/// same set Core registers its documents from. A name it does not fetch is a document whose routes the oracle
/// answers <c>404 route.not_found</c> although they are served; a name Core does not publish keeps the whole
/// surface from loading.
/// </summary>
public sealed class RouteSurfaceLoaderDocumentNamesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task Should_FetchOneDocumentPerPublishedName_When_LoadingTheSurface(string? v1ApiFlag, bool expectedV1ApiEnabled)
    {
        var expected = ApiDocuments.Published(expectedV1ApiEnabled).Select(name => $"/api/{name}/openapi.json").Order(StringComparer.Ordinal);

        var requested = await RunOneLoadAsync(v1ApiFlag);

        Assert.Equal(expected, requested.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    /// <summary>
    /// The mismatch ADR-0106 fails towards 404: the gateway lists a legacy document the API no longer
    /// publishes. Nothing is published, and the refusal is a Warning naming the document, so the operator
    /// can see which side carries the wrong <c>FeatureManagement__V1Api</c>.
    /// </summary>
    [Fact]
    public async Task Should_LogAWarningNamingTheDocument_When_AListedDocumentIsRefused()
    {
        const string refusedPath = "/api/User/openapi.json";
        var requested = new ConcurrentBag<string>();
        using var logs = new CapturingLoggerProvider();
        using var handler = new RecordingHandler(requested, refusedPath);
        var provider = new RouteSurfaceProvider();

        await using var container = BuildContainer("true", handler, provider, logs);
        var loader = container.GetServices<IHostedService>().OfType<RouteSurfaceLoader>().Single();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await loader.StartAsync(cancellation.Token);

        while (!logs.From<RouteSurfaceLoader>().Any(entry => entry.Level == LogLevel.Warning))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellation.Token);
        }

        await loader.StopAsync(CancellationToken.None);

        var warning = Assert.Single(logs.From<RouteSurfaceLoader>(), entry => entry.Level == LogLevel.Warning);
        Assert.Contains(refusedPath, warning.Message, StringComparison.Ordinal);
        Assert.Contains("404", warning.Message, StringComparison.Ordinal);
        Assert.Same(RouteSurface.Empty, provider.Current);
    }

    /// <summary>
    /// Runs the loader the gateway registers until it has published a surface, and reports every path it
    /// asked the upstream for.
    /// </summary>
    private static async Task<IReadOnlyCollection<string>> RunOneLoadAsync(string? v1ApiFlag)
    {
        var requested = new ConcurrentBag<string>();
        var provider = new RouteSurfaceProvider();
        using var logs = new CapturingLoggerProvider();
        using var handler = new RecordingHandler(requested, refusedPath: null);

        await using var container = BuildContainer(v1ApiFlag, handler, provider, logs);
        var loader = container.GetServices<IHostedService>().OfType<RouteSurfaceLoader>().Single();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await loader.StartAsync(cancellation.Token);

        // why: waiting on the published surface rather than on a request count lets a loader reading a shorter list fail on the assertion instead of hanging until the timeout.
        while (ReferenceEquals(provider.Current, RouteSurface.Empty))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellation.Token);
        }

        await loader.StopAsync(CancellationToken.None);

        return [.. requested];
    }

    /// <summary>
    /// The gateway's own registration of the oracle, with the flag supplied through configuration as the
    /// gateway host receives it and only the upstream replaced.
    /// </summary>
    private static ServiceProvider BuildContainer(string? v1ApiFlag, HttpMessageHandler handler, RouteSurfaceProvider provider, ILoggerProvider logs)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        if (v1ApiFlag is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureManagement:V1Api"] = v1ApiFlag,
            });
        }

        builder.Logging.AddProvider(logs);
        builder.AddRouteExistenceFeature();
        builder.Services.AddSingleton(provider);
        builder.Services.AddHttpClient(RouteSurfaceLoader.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return builder.Services.BuildServiceProvider();
    }

    /// <summary>
    /// Answers every request with a document describing no path, so the load succeeds and the only thing
    /// the test observes is which documents were asked for; <paramref name="refusedPath"/> is answered 404.
    /// </summary>
    private sealed class RecordingHandler(ConcurrentBag<string> requested, string? refusedPath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            requested.Add(path);

            if (string.Equals(path, refusedPath, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"paths":{}}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}