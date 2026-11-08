using System.Net;

using Waldur.Sync.Api;
using Waldur.Sync.Configuration;
using Waldur.Sync.Http;
using Waldur.Sync.Tests.TestSupport;

using Xunit;

namespace Waldur.Sync.Tests;

public class WaldurApiClientTests
{
    private static WaldurInstanceOptions Options() => new()
    {
        Name = "EFP",
        Enabled = true,
        BaseUrl = new Uri("https://waldur.test/"),
        CustomersEndpoint = new Uri("/api/customers/", UriKind.Relative),
        ProjectsEndpoint = new Uri("/api/projects/", UriKind.Relative),
        MarketplacePublicOfferingsEndpoint = new Uri("/api/marketplace-public-offerings/", UriKind.Relative),
        MarketplaceResourcesEndpoint = new Uri("/api/marketplace-resources/", UriKind.Relative)
    };

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) =>
        new(handler, disposeHandler: false) { BaseAddress = new Uri("https://waldur.test/") };

    private static string Customers(int count) =>
        $"[{string.Join(",", Enumerable.Range(0, count).Select(i => $$"""{"uuid":"{{Guid.NewGuid():N}}","name":"c{{i}}","display_name":"C{{i}}","description":null,"country":"CZ","created":"2024-01-01T00:00:00Z"}"""))}]";

    [Fact]
    public async Task Should_ReturnDeserializedItems_When_SinglePageSucceeds()
    {
        using var handler = new SequenceHandler((HttpStatusCode.OK, Customers(3)));
        using var http = CreateHttpClient(handler);
        var client = new WaldurApiClient(http, Options());

        var customers = await client.GetCustomersAsync(CancellationToken.None);

        Assert.True(customers.IsComplete);
        Assert.Equal(3, customers.Items.Count);
        Assert.All(customers.Items, c => Assert.NotEqual(Guid.Empty, c.Id));
    }

    [Fact]
    public async Task Should_ReturnIncompleteEmpty_When_FirstPageFails()
    {
        // ADR-0064: the resilient list fetch no longer throws on a page failure — a failed first page
        // yields an empty, incomplete result so the caller suppresses the absence sweep.
        using var handler = new SequenceHandler((HttpStatusCode.InternalServerError, ""));
        using var http = CreateHttpClient(handler);
        var client = new WaldurApiClient(http, Options());

        var customers = await client.GetCustomersAsync(CancellationToken.None);

        Assert.False(customers.IsComplete);
        Assert.Empty(customers.Items);
    }

    [Fact]
    public async Task Should_ReturnPartialIncomplete_When_LaterPageFails()
    {
        // Page 1 is a full page (100) → the client requests page 2, which fails. ADR-0064: it returns
        // the 100 already-fetched items flagged incomplete rather than throwing or silently truncating.
        using var handler = new SequenceHandler(
            (HttpStatusCode.OK, Customers(100)),
            (HttpStatusCode.BadGateway, ""));
        using var http = CreateHttpClient(handler);
        var client = new WaldurApiClient(http, Options());

        var customers = await client.GetCustomersAsync(CancellationToken.None);

        Assert.False(customers.IsComplete);
        Assert.Equal(100, customers.Items.Count);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Should_StopPagingComplete_When_ShortPageReturned()
    {
        using var handler = new SequenceHandler((HttpStatusCode.OK, Customers(42)));
        using var http = CreateHttpClient(handler);
        var client = new WaldurApiClient(http, Options());

        var customers = await client.GetCustomersAsync(CancellationToken.None);

        Assert.True(customers.IsComplete);
        Assert.Equal(42, customers.Items.Count);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Should_ReturnIncomplete_When_NetworkFails()
    {
        using var handler = new ThrowingHandler();
        using var http = CreateHttpClient(handler);
        var client = new WaldurApiClient(http, Options());

        var projects = await client.GetProjectsAsync(CancellationToken.None);

        Assert.False(projects.IsComplete);
        Assert.Empty(projects.Items);
    }

    [Fact]
    public async Task Should_ThrowOnEmptyBody_When_OfferingDetailMissing()
    {
        using var handler = new SequenceHandler((HttpStatusCode.OK, "null"));
        using var http = CreateHttpClient(handler);
        var client = new WaldurApiClient(http, Options());

        await Assert.ThrowsAsync<WaldurApiException>(() =>
            client.GetOfferingDetailAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("boom");
    }
}
