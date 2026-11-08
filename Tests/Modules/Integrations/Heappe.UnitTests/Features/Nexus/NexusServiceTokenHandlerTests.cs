using System.Net;
using System.Text.Json;

using Heappe.Features.Connection;
using Heappe.Features.Nexus;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Xunit;

namespace Heappe.UnitTests.Features.Nexus;

/// <summary>
/// UserOrg calls the Nexus registry as its own service account: the handler attaches a bearer token from
/// <c>POST /api/v1/auth/token</c>, reuses it until it expires, and on a 401 refreshes it and retries exactly once.
/// </summary>
public sealed class NexusServiceTokenHandlerTests
{
    private const string TokenPath = "/api/v1/auth/token";
    private const string RegistryPath = "/api/v1/heappe";
    private const int ExpiresInSeconds = 300;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private int _issued;

    [Fact]
    public async Task Handler_reuses_the_token_before_it_expires()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tokenEndpoint = TokenEndpoint();
        var registry = new FakeHttpEndpoint().On(HttpMethod.Get, RegistryPath, _ => FakeHttpEndpoint.Json("[]"));
        using var handler = Handler(tokenEndpoint, registry);
        using var client = Client(handler);

        using var first = await client.GetAsync(new Uri(RegistryPath, UriKind.Relative), ct);
        _time.Advance(TimeSpan.FromSeconds(60));
        using var second = await client.GetAsync(new Uri(RegistryPath, UriKind.Relative), ct);

        Assert.Equal(1, tokenEndpoint.CallsTo(TokenPath));
        Assert.Equal(["Bearer tok-1", "Bearer tok-1"], registry.Authorizations);
    }

    [Fact]
    public async Task Handler_fetches_a_new_token_once_the_old_one_expired()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tokenEndpoint = TokenEndpoint();
        var registry = new FakeHttpEndpoint().On(HttpMethod.Get, RegistryPath, _ => FakeHttpEndpoint.Json("[]"));
        using var handler = Handler(tokenEndpoint, registry);
        using var client = Client(handler);

        using var first = await client.GetAsync(new Uri(RegistryPath, UriKind.Relative), ct);
        _time.Advance(TimeSpan.FromSeconds(ExpiresInSeconds + 1));
        using var second = await client.GetAsync(new Uri(RegistryPath, UriKind.Relative), ct);

        Assert.Equal(2, tokenEndpoint.CallsTo(TokenPath));
        Assert.Equal(["Bearer tok-1", "Bearer tok-2"], registry.Authorizations);
    }

    [Fact]
    public async Task Handler_refreshes_the_token_and_retries_once_when_nexus_answers_401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tokenEndpoint = TokenEndpoint();
        var registry = new FakeHttpEndpoint().On(HttpMethod.Get, RegistryPath, request =>
            request.Headers.Authorization?.Parameter == "tok-1"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : FakeHttpEndpoint.Json("[]"));
        using var handler = Handler(tokenEndpoint, registry);
        using var client = Client(handler);

        using var response = await client.GetAsync(new Uri(RegistryPath, UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, tokenEndpoint.CallsTo(TokenPath));
        Assert.Equal(["Bearer tok-1", "Bearer tok-2"], registry.Authorizations);
    }

    [Fact]
    public async Task Handler_retries_a_401_only_once()
    {
        var ct = TestContext.Current.CancellationToken;
        using var tokenEndpoint = TokenEndpoint();
        var registry = new FakeHttpEndpoint().On(HttpMethod.Get, RegistryPath, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var handler = Handler(tokenEndpoint, registry);
        using var client = Client(handler);

        using var response = await client.GetAsync(new Uri(RegistryPath, UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, registry.CallsTo(RegistryPath));
        Assert.Equal(2, tokenEndpoint.CallsTo(TokenPath));
    }

    [Fact]
    public async Task Provider_requests_the_token_with_the_TokenExchange_Nexus_client_credentials()
    {
        string? tokenRequestBody = null;
        using var tokenEndpoint = new FakeHttpEndpoint().On(HttpMethod.Post, TokenPath, async request =>
        {
            tokenRequestBody = await request.Content!.ReadAsStringAsync();
            return IssueToken();
        });

        await Provider(tokenEndpoint).GetTokenAsync(forceRefresh: false, TestContext.Current.CancellationToken);

        Assert.NotNull(tokenRequestBody);
        using var body = JsonDocument.Parse(tokenRequestBody);
        Assert.Equal("userorg", body.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("userorg-secret", body.RootElement.GetProperty("client_secret").GetString());
    }

    private FakeHttpEndpoint TokenEndpoint() =>
        new FakeHttpEndpoint().On(HttpMethod.Post, TokenPath, _ => IssueToken());

    private HttpResponseMessage IssueToken()
    {
        var token = $"tok-{Interlocked.Increment(ref _issued)}";
        return FakeHttpEndpoint.Json($$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{ExpiresInSeconds}}}""");
    }

    private NexusServiceTokenProvider Provider(FakeHttpEndpoint tokenEndpoint) =>
        new(
            new FakeHttpClientFactory(tokenEndpoint, NexusRegistryFake.RegistryBaseAddress),
            Options.Create(new TokenExchangeOptions { Nexus = { ClientId = "userorg", ClientSecret = "userorg-secret" } }),
            _time);

    private NexusServiceTokenHandler Handler(FakeHttpEndpoint tokenEndpoint, FakeHttpEndpoint registry) =>
        new(Provider(tokenEndpoint)) { InnerHandler = registry };

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false)
        {
            BaseAddress = NexusRegistryFake.RegistryBaseAddress
        };
}
