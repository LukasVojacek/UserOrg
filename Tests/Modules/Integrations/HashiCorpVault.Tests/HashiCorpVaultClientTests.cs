using System.Net;
using System.Text;
using System.Text.Json;

using HashiCorpVault.Contracts.Models;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using UserOrg.Shared;

using Xunit;

namespace HashiCorpVault.Tests;

public sealed class HashiCorpVaultClientTests
{
    private sealed class TestHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = [];
        public TestHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    private static IHttpClientFactory CreateFactory(HttpMessageHandler handler)
    {
        // ponytail: no `using` — the returned factory hands this client to the SUT, which uses it after this method returns
        var client = new HttpClient(handler) { BaseAddress = new System.Uri("http://localhost/") };
        return new SimpleFactory(client);
    }

    private sealed class SimpleFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SimpleFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private static IOptions<VaultOptions> CreateOptions(string mount = "UserOrg_kv")
        => Options.Create(new VaultOptions { MountPoint = mount });

    [Fact(DisplayName = "UpdateServiceAccessKeysBulkAsync creates and deletes DDI entries appropriately")]
    public async Task UpdateServiceAccessKeysBulkAsyncCreateAndDeleteBehavior()
    {
        // existing vault has one DDI entry "ExistingDDI" and one non-DDI "OtherService"
        Dictionary<string, object> existing = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["ExistingDDI"] = new ServiceAccessKeys { Name = "ExistingDDI", BackupKey = "b1", Key = "k1", ServiceType = EServiceType.DDI, Data = "{}", ContactEmail = "" },
            ["OtherService"] = new ServiceAccessKeys { Name = "OtherService", BackupKey = "b2", Key = "k2", ServiceType = EServiceType.Service, Data = "{}", ContactEmail = "contact@ex" }
        };

        var getPayload = new { data = new { data = existing } };
        var getJson = JsonSerializer.Serialize(getPayload);

        string? lastPostBody = null;

        using TestHttpHandler handler = new(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/data/ServiceAccessKeys"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(getJson, Encoding.UTF8, "application/json") };
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/data/ServiceAccessKeys"))
            {
                lastPostBody = req.Content?.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = CreateFactory(handler);
        HashiCorpVaultClient client = new(factory, CreateOptions(), NullLogger<HashiCorpVaultClient>.Instance);

        List<DataStagingLocationKey> incoming = new()
        {
            new DataStagingLocationKey { Name = "NewDDI", Key = "knew", BackupKey = "bnew", JsonData = "{\"x\":1}" }
        };

        var result = await client.UpdateServiceAccessKeysBulkAsync(incoming);
        Assert.True(result.IsSuccess);
        var counts = result.Value;
        Assert.Equal(1, counts.Created);
        Assert.Equal(0, counts.Updated);
        Assert.Equal(1, counts.Deleted);

        Assert.NotNull(lastPostBody);
        using JsonDocument doc = JsonDocument.Parse(lastPostBody!);
        Assert.True(doc.RootElement.TryGetProperty("data", out var data));
        // final map should contain OtherService (preserved) and NewDDI (incoming)
        List<string> names = data.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("OtherService", names);
        Assert.Contains("NewDDI", names);
        Assert.DoesNotContain("ExistingDDI", names);
    }

    [Fact(DisplayName = "UpdateServiceAccessKeysBulkAsync treats NotFound as empty and creates all incoming entries")]
    public async Task UpdateServiceAccessKeysBulkAsyncNotFoundCreatesAll()
    {
        string? lastPostBody = null;

        using TestHttpHandler handler = new(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("/data/ServiceAccessKeys"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/data/ServiceAccessKeys"))
            {
                lastPostBody = req.Content?.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = CreateFactory(handler);
        HashiCorpVaultClient client = new(factory, CreateOptions(), NullLogger<HashiCorpVaultClient>.Instance);

        List<DataStagingLocationKey> incoming = new()
        {
            new DataStagingLocationKey { Name = "A", Key = "kA", BackupKey = "bA", JsonData = "{}" },
            new DataStagingLocationKey { Name = "B", Key = "kB", BackupKey = "bB", JsonData = "{}" }
        };

        var result = await client.UpdateServiceAccessKeysBulkAsync(incoming);
        Assert.True(result.IsSuccess);
        var counts = result.Value;
        Assert.Equal(2, counts.Created);
        Assert.Equal(0, counts.Updated);
        Assert.Equal(0, counts.Deleted);

        Assert.NotNull(lastPostBody);
        using JsonDocument doc = JsonDocument.Parse(lastPostBody!);
        Assert.True(doc.RootElement.TryGetProperty("data", out var data));
        List<string> names = data.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("A", names);
        Assert.Contains("B", names);
    }
}