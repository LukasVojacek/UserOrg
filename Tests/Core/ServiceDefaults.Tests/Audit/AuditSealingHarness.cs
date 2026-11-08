using System.Net;
using System.Text;
using System.Text.Json;

using Lukdrasil.StepUpLogging;
using Lukdrasil.StepUpLogging.Audit.EncryptedSpool;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Ogham.Sealing;
using Ogham.Sealing.AspNetCore;

using UserOrg.ServiceDefaults.Audit;

namespace ServiceDefaults.Tests.Audit;

/// <summary>
/// Stands up both halves of the real chain over a throwaway key pair: an unsealing side that
/// generates a key and publishes the key document, and a producer side whose encrypted spool
/// sink hands its own serialized payload to <see cref="AuditSqlSealer"/>. Nothing about the
/// payload's wire shape is assumed - the library writes it - and no sealing is simulated.
/// </summary>
public sealed class AuditSealingHarness : IAsyncLifetime
{
    internal const string ModuleName = "UserOrg.Api";
    internal const string ModuleVersion = "1.4.2+ci";
    internal const string ImmuDbUsername = "immudb";
    // Drawn from the base64 alphabet on purpose: the plaintext scan reads a base64-encoded
    // spool file, so a probe holding a character base64 can never emit would pass by framing.
    internal const string ImmuDbPassword = "n0tInTheClearPw";
    internal const string ImmuDbDatabase = "auditdb";

    private static readonly TimeSpan KeyWait = TimeSpan.FromSeconds(30);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "userorg-audit-sealing", Guid.NewGuid().ToString("N"));
    private readonly string _spoolDirectory;

    private readonly ServiceProvider _unsealingSide;

    private IHost? _producer;
    private IAuditEventSink _sink = null!;

    public AuditSealingHarness()
    {
        _spoolDirectory = Path.Combine(_root, "spool");
        _unsealingSide = BuildUnsealingSide();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "IDisposableAnalyzers.Correctness", "IDISP003:Dispose previous before re-assigning",
        Justification = "Fields are assigned exactly once during this one-shot lifecycle init and disposed in DisposeAsync.")]
    public async ValueTask InitializeAsync()
    {
        foreach (var rotation in _unsealingSide.GetServices<IHostedService>())
        {
            await rotation.StartAsync(CancellationToken.None);
        }

        var producer = BuildProducer(await WaitForAPublishedKeyAsync(_unsealingSide.GetRequiredService<ISealingKeyDocument>()));
        await producer.StartAsync();

        _producer = producer;
        _sink = producer.Services.GetRequiredService<IAuditEventSink>();
    }

    public async ValueTask DisposeAsync()
    {
        if (_producer is not null)
        {
            await _producer.StopAsync();
            _producer.Dispose();
        }

        await _unsealingSide.DisposeAsync();

        Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// Audits <paramref name="auditEvent"/> through the real sink and recovers what the audit
    /// unsealing proxy would see: the spool file exactly as it hit the disk, and the one record
    /// inside its envelope.
    /// </summary>
    internal async Task<SealedAudit> AuditAsync(AuditEvent auditEvent)
    {
        var before = Directory.GetFiles(_spoolDirectory);
        Assert.Equal(AuditWriteResult.Stored, await _sink.WriteAsync(auditEvent));

        var spoolFile = Assert.Single(Directory.GetFiles(_spoolDirectory).Except(before, StringComparer.Ordinal));
        var spoolFileBytes = await File.ReadAllBytesAsync(spoolFile);
        var envelope = JsonSerializer.Deserialize<JsonElement>(spoolFileBytes).GetProperty("payload").GetBytesFromBase64();

        var unsealed = await _unsealingSide.GetRequiredService<IUnsealer>().UnsealAsync(envelope);
        Assert.Equal(UnsealFailure.None, unsealed.Failure);

        using var record = JsonDocument.Parse(Assert.Single(unsealed.Records));
        return new SealedAudit(spoolFileBytes, envelope, record.RootElement.Clone());
    }

    private ServiceProvider BuildUnsealingSide()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(_root, "ring")));
        services.AddOghamUnsealing(options =>
        {
            options.RotationCheckPeriod = TimeSpan.FromMilliseconds(50);
            options.KeyDocumentCacheDuration = TimeSpan.FromMilliseconds(50);
        });
        services.AddDirectorySealingKeyStore(Path.Combine(_root, "keys"));
        return services.BuildServiceProvider();
    }

    private IHost BuildProducer(string keyDocument)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddOghamSealing(options =>
        {
            options.KeyEndpoint = new Uri("https://audit-proxy.invalid/.well-known/ogham-sealing-keys.json");
            options.CacheDirectory = Path.Combine(_root, "key-cache");
            options.RequireKeyAtStartup = true;
            options.StartupKeyTimeout = KeyWait;
        });
        builder.Services.AddHttpClient(SealingOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StaticKeyDocumentHandler(keyDocument));
        builder.Services.AddSingleton(new ImmuDbCredentials(ImmuDbUsername, ImmuDbPassword, ImmuDbDatabase));
        builder.Services.AddSingleton<IAuditPayloadEncryptor, AuditSqlSealer>();
        builder.AddStepUpLogging();
        builder.AddEncryptedSpoolAuditSink(options =>
        {
            options.SpoolDirectory = _spoolDirectory;
            options.EndpointBaseUrl = "https://audit-proxy.invalid";
            options.ModuleName = ModuleName;
            options.Version = ModuleVersion;

            // The proxy is unreachable here on purpose; a long interval keeps the drain worker
            // from touching the very spool files these tests read back.
            options.DrainInterval = TimeSpan.FromHours(1);
        });

        return builder.Build();
    }

    private static async Task<string> WaitForAPublishedKeyAsync(ISealingKeyDocument document)
    {
        using var giveUp = new CancellationTokenSource(KeyWait);
        while (true)
        {
            var json = await document.GetJsonAsync(giveUp.Token);
            using (var published = JsonDocument.Parse(json))
            {
                if (published.RootElement.GetProperty("keys").GetArrayLength() > 0)
                {
                    return json;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), giveUp.Token);
        }
    }

    private sealed class StaticKeyDocumentHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}

/// <summary>What the drain worker would ship, and what the proxy would read out of it.</summary>
/// <param name="SpoolFileBytes">The spool file verbatim, for scanning it for plaintext.</param>
/// <param name="EnvelopeBytes">The spool file's base64 payload decoded - the sealed envelope itself.</param>
/// <param name="Content">The single unsealed record: credentials plus the sqlexec request.</param>
internal sealed record SealedAudit(byte[] SpoolFileBytes, byte[] EnvelopeBytes, JsonElement Content);