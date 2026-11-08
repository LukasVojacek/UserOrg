using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

using Lukdrasil.StepUpLogging;
using Lukdrasil.StepUpLogging.Audit.EncryptedSpool;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using UserOrg.ServiceDefaults;

using WireMock.Server;

namespace IntegrationTests.Audit;

/// <summary>
/// A producer whose key cache was warmed by an earlier proxy store must not seal with that store's key:
/// it has to fetch the current proxy's key before its first seal, so the record unseals and drains into
/// immugw instead of being rejected for an unknown key and dead-lettered (ADR-0116).
/// </summary>
public sealed class AuditWarmCacheTests
{
    private const string ImmuDbUsername = "e2e-tests";
    private const string ImmuDbPassword = "e2e-tests";
    private const string ImmuDbDatabase = "warm-cache";
    private const string ImmuGwToken = "t";
    private const string SqlExecPath = $"/db/{ImmuDbDatabase}/sqlexec";
    private const string ProducerName = "UserOrg.Api";

    // SEALING.md §4: the byte at offset 6 declares the kid's length and the kid starts at offset 7.
    private const int KidLengthOffset = 6;
    private const int KidOffset = 7;

    private static readonly TimeSpan KeyPublicationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Should_SealWithTheCurrentKey_And_DrainTheSpool_When_TheCacheIsFromAnEarlierProxyStore()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("userorg-e2e-warm-cache-").FullName;
        var auditDirectory = Path.Combine(root, "audit");
        var keyCacheDirectory = Path.Combine(auditDirectory, "key-cache");
        var spoolDirectory = Path.Combine(auditDirectory, "spool");

        using var immuGw = StartImmuGwStub();
        var immuGwPort = (ushort)immuGw.Ports[0];
        await TestcontainersSettings.ExposeHostPortsAsync(immuGwPort, ct);

        await using var proxyA = BuildProxy(immuGwPort);
        await proxyA.StartAsync(ct);
        var keysA = await ReadKeysAsync(UrlOf(proxyA), ct);

        using (var producer1 = BuildProducer(UrlOf(proxyA), root))
        {
            await StopInReverseAsync(await StartAllButDrainWorkerAsync(producer1));
        }

        await proxyA.StopAsync(ct);

        await using var proxyB = BuildProxy(immuGwPort);
        await proxyB.StartAsync(ct);
        var keysB = await ReadKeysAsync(UrlOf(proxyB), ct);

        Assert.NotEqual(keysA.StoreEpoch, keysB.StoreEpoch);
        Assert.NotEqual(keysA.Kid, keysB.Kid);

        using var producer2 = BuildProducer(UrlOf(proxyB), root);
        var started = await StartAllButDrainWorkerAsync(producer2);
        try
        {
            var written = await producer2.Services.GetRequiredService<IAuditEventSink>()
                .WriteAsync(AuditEvent.Success("audit.warm-cache-probe", "actor-1", "user"));

            Assert.Equal(AuditWriteResult.Stored, written);
            var spoolFile = Assert.Single(Directory.GetFiles(spoolDirectory, "*.env"));
            Assert.Equal(keysB.Kid, KidOf(spoolFile));

            var drainWorker = Assert.Single(
                producer2.Services.GetServices<IHostedService>(), service => service.GetType().Name == "DrainWorker");
            await drainWorker.StartAsync(ct);
            started = [.. started, drainWorker];

            await WaitUntilAsync(() => Directory.GetFiles(spoolDirectory).Length == 0, DrainTimeout, ct);

            Assert.Contains(immuGw.LogEntries, entry => entry.RequestMessage is { Method: "POST", Path: SqlExecPath });
            Assert.DoesNotContain(
                Directory.EnumerateFiles(auditDirectory, "*", SearchOption.AllDirectories),
                file => !file.StartsWith(keyCacheDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }
        finally
        {
            await StopInReverseAsync(started);
            Directory.Delete(root, recursive: true);
        }
    }

    private static WireMockServer StartImmuGwStub()
    {
        var server = WireMockServer.Start();
        var token = JsonSerializer.Serialize(new { token = ImmuGwToken });

        server.Given(Request.Create().WithPath("/login").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(token));
        server.Given(Request.Create().WithPath($"/db/use/{ImmuDbDatabase}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(token));
        server.Given(Request.Create().WithPath(SqlExecPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("{}"));

        return server;
    }

    // The fixture's proxy, with its immugw pointed at the stub on this host: the drain only empties the
    // spool once immugw has confirmed the write, so delivery is observable at the stub.
    private static IContainer BuildProxy(ushort immuGwPort) => new ContainerBuilder(TestcontainersTopologyFixture.AuditProxyImage)
        .WithPortBinding(TestcontainersTopologyFixture.AuditProxyContainerPort, assignRandomHostPort: true)
        .WithEnvironment("VaultKeyRing__Mode", "File")
        .WithEnvironment("VaultKeyRing__Directory", TestcontainersTopologyFixture.ContainerKeyRingDirectory)
        .WithEnvironment("SealingKeyStoreDirectory", TestcontainersTopologyFixture.ContainerSealingKeyStoreDirectory)
        .WithEnvironment("ImmuGwBaseUrl", $"http://host.testcontainers.internal:{immuGwPort}")
        .Build();

    private static string UrlOf(IContainer proxy) =>
        $"http://127.0.0.1:{proxy.GetMappedPublicPort(TestcontainersTopologyFixture.AuditProxyContainerPort)}";

    // A fresh store answers its key document before it has minted a key, so an empty document is polled past.
    private static async Task<PublishedKeys> ReadKeysAsync(string proxyUrl, CancellationToken ct)
    {
        using var client = new HttpClient { BaseAddress = new Uri(proxyUrl), Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow + KeyPublicationTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await TryReadKeysAsync(client, ct) is { } keys)
                return keys;

            await Task.Delay(PollInterval, ct);
        }

        throw new InvalidOperationException($"The audit proxy at {proxyUrl} published no sealing key within {KeyPublicationTimeout}.");
    }

    // The current key is the newest one, the same choice the producer's key cache makes.
    private static async Task<PublishedKeys?> TryReadKeysAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await client.GetStringAsync(new Uri(TestcontainersTopologyFixture.SealingKeyDocumentPath, UriKind.Relative), ct));
            using var entries = document.RootElement.GetProperty("keys").EnumerateArray();
            var keys = entries.ToList();
            return keys.Count == 0
                ? null
                : new PublishedKeys(
                    keys.MaxBy(key => key.GetProperty("createdAt").GetDateTimeOffset()).GetProperty("kid").ToString(),
                    document.RootElement.GetProperty("storeEpoch").ToString());
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static IHost BuildProducer(string proxyUrl, string root)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = ProducerName,
            ContentRootPath = root
        });

        builder.Configuration.AddInMemoryCollection([
            new("ImmuDb:Username", ImmuDbUsername),
            new("ImmuDb:Password", ImmuDbPassword),
            new("ImmuDb:Database", ImmuDbDatabase),
            new("EncryptedSpool:SpoolDirectory", Path.Combine(root, "audit", "spool")),
            new("EncryptedSpool:EndpointBaseUrl", proxyUrl),
            new("EncryptedSpool:DrainInterval", "00:00:01"),
            new("OghamSealing:KeyEndpoint", $"{proxyUrl}{TestcontainersTopologyFixture.SealingKeyDocumentPath}"),
            new("OghamSealing:CacheDirectory", Path.Combine(root, "audit", "key-cache")),
            new("OghamSealing:RequireKeyAtStartup", "true"),
            new("OghamSealing:StartupKeyTimeout", "00:00:30"),
            new("SerilogStepUp:EnableOtlpExporter", "false")
        ]);

        builder.AddServiceDefaults();
        return builder.Build();
    }

    // Started by hand so the drain worker can be held back until the spooled envelope has been read.
    private static async Task<IReadOnlyList<IHostedService>> StartAllButDrainWorkerAsync(IHost host)
    {
        var started = new List<IHostedService>();
        foreach (var service in host.Services.GetServices<IHostedService>().Where(service => service.GetType().Name != "DrainWorker"))
        {
            await service.StartAsync(CancellationToken.None);
            started.Add(service);
        }

        return started;
    }

    private static async Task StopInReverseAsync(IReadOnlyList<IHostedService> started)
    {
        foreach (var service in started.Reverse())
            await service.StopAsync(CancellationToken.None);
    }

    private static string KidOf(string spoolFile)
    {
        var envelope = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(spoolFile)).GetProperty("payload").GetBytesFromBase64();
        return System.Text.Encoding.ASCII.GetString(envelope, KidOffset, envelope[KidLengthOffset]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(PollInterval, ct);
    }

    private sealed record PublishedKeys(string Kid, string StoreEpoch);
}
