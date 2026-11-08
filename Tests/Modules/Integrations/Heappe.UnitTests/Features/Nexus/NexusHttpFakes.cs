using System.Collections.Concurrent;
using System.Net;
using System.Text;

using Heappe.Features.Connection;
using Heappe.Features.Nexus;

using Microsoft.Extensions.Options;

namespace Heappe.UnitTests.Features.Nexus;

/// <summary>
/// The HTTP boundary of NexusApi (and of a HEAppE instance behind its proxy) for unit tests: answers by
/// method and absolute path, ignoring the host, so it works whether the caller sends a relative path against a
/// base address or the absolute <c>{TokenExchange:Nexus:BaseUrl}/...</c> address.
/// </summary>
internal sealed class FakeHttpEndpoint : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);
    private HttpClient? _client;

    /// <summary>The absolute request URIs in the order they arrived.</summary>
    public ConcurrentQueue<Uri> RequestUris { get; } = new();

    /// <summary>The <c>Authorization</c> header of every request, in order (<c>null</c> when absent).</summary>
    public ConcurrentQueue<string?> Authorizations { get; } = new();

    public FakeHttpEndpoint On(HttpMethod method, string path, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        On(method, path, request => Task.FromResult(respond(request)));

    public FakeHttpEndpoint On(HttpMethod method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        _routes[Key(method, Rooted(path))] = respond;
        return this;
    }

    public int CallsTo(string path) => _calls.GetValueOrDefault(Rooted(path));

    /// <summary>One client over this endpoint, created on first use and disposed with the endpoint.</summary>
    public HttpClient ClientFor(Uri baseAddress) =>
        _client ??= new HttpClient(this, disposeHandler: false) { BaseAddress = baseAddress };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        _calls.AddOrUpdate(path, 1, (_, count) => count + 1);
        RequestUris.Enqueue(request.RequestUri);
        Authorizations.Enqueue(request.Headers.Authorization?.ToString());

        if (_routes.TryGetValue(Key(request.Method, path), out var respond))
        {
            return await respond(request);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _client?.Dispose();
        }

        base.Dispose(disposing);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Problem(HttpStatusCode status, string problemType) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"type":"{{problemType}}","title":"problem","status":{{(int)status}}}""",
                Encoding.UTF8,
                "application/problem+json")
        };

    private static string Rooted(string path) => path.StartsWith('/') ? path : $"/{path}";

    private static string Key(HttpMethod method, string path) => $"{method.Method} {path}";
}

/// <summary>An <see cref="IHttpClientFactory"/> that hands out clients over one fake endpoint, whatever the name.</summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
}

/// <summary>Builds the real <see cref="HeappeInstanceDirectory"/> over a fake Nexus registry.</summary>
internal static class NexusRegistryFake
{
    public const string HeappeInstancesPath = "/api/v1/heappe";
    public const string ClientsPath = "/api/v1/clients";

    /// <summary>The <c>TokenExchange:Nexus:BaseUrl</c> every fake in these tests is configured with.</summary>
    public static readonly Uri NexusBaseUrl = new("http://nexus.test/");

    public static readonly Uri RegistryBaseAddress = NexusBaseUrl;

    /// <summary>The proxy base every HEAppE call for <c>heappe-mock</c> must use: <c>{BaseUrl}/api/heappe/heappe-mock/</c>.</summary>
    public static readonly Uri HeappeMockProxyBase = new("http://nexus.test/api/heappe/heappe-mock/");

    public const string HeappeMockInstances = """
        [
          { "name": "heappe-mock", "url": "http://heappe-mock:8080", "enabled": true },
          { "name": "heappe-off", "url": "http://heappe-off:8080", "enabled": false }
        ]
        """;

    // The client name is upper case on purpose: the join to an instance is case-insensitive.
    public const string HeappeMockClients = """
        [
          { "name": "HEAPPE-MOCK", "clientId": "heappe-mock-client", "scopes": ["openid", "heappe"] }
        ]
        """;

    public static FakeHttpEndpoint Registry(string instancesJson = HeappeMockInstances, string clientsJson = HeappeMockClients) =>
        new FakeHttpEndpoint()
            .On(HttpMethod.Get, HeappeInstancesPath, _ => FakeHttpEndpoint.Json(instancesJson))
            .On(HttpMethod.Get, ClientsPath, _ => FakeHttpEndpoint.Json(clientsJson));

    /// <summary>The options as <c>TokenExchange:Nexus</c> binds them: the base URL and the registry snapshot lifetime.</summary>
    public static IOptions<TokenExchangeOptions> Settings(Uri? baseUrl = null, TimeSpan? directoryCacheTtl = null) =>
        Options.Create(new TokenExchangeOptions
        {
            Nexus =
            {
                BaseUrl = baseUrl ?? NexusBaseUrl,
                CacheMapTtl = directoryCacheTtl ?? TimeSpan.FromSeconds(60),
            },
        });

    public static HeappeInstanceDirectory Directory(FakeHttpEndpoint registry, TimeProvider? timeProvider = null, IOptions<TokenExchangeOptions>? settings = null) =>
        new(
            new NexusRegistryClient(registry.ClientFor(RegistryBaseAddress)),
            new FakeHybridCache(),
            settings ?? Settings(),
            timeProvider ?? TimeProvider.System);

    /// <summary>The flag-off directory: every identifier goes to <c>{BaseUrl}/api/heappe/{identifier}/</c>, no registry.</summary>
    public static ProxyOnlyHeappeInstanceDirectory ProxyOnly(Uri? baseUrl = null) => new(Settings(baseUrl));
}
