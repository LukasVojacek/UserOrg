using System.Collections;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Authentication;
using UserOrg.Gateway.Configuration;
using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins the gateway's operational audit records (T-208-11): signing-key generation and rotation, API-key
/// authentication failures and backup-key successes, and dev-override impersonation. Every write is fail-open,
/// so each record also has a case where the sink throws and the operation must still complete with one Warning.
/// </summary>
public sealed class GatewayOperationalAuditTests
{
    private const string PrimaryKey = "primary-key-value-4711";

    private const string BackupKey = "backup-key-value-0815";

    private const string UnknownKey = "unknown-key-value-2342";

    private const string ServiceName = "svc-audit";

    private const string ImpersonatedSub = "idp-user-123";

    private static readonly TimeSpan RotationDeadline = TimeSpan.FromSeconds(5);

    private sealed record LogEntry(LogLevel Level, string Text);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed class CryptoFailingHybridCache : HybridCache
    {
        private readonly ConcreteHybridCache _inner = new();

        public bool FailWrites { get; set; }

        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => _inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);

        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
            => FailWrites
                ? throw new CryptographicException("key material could not be cached")
                : _inner.SetAsync(key, value, options, tags, cancellationToken);

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
            => _inner.RemoveAsync(key, cancellationToken);

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
            => _inner.RemoveByTagAsync(tag, cancellationToken);
    }

    private sealed record Harness(ServiceProvider Services, RecordingAuditEventSink Sink, RecordingLoggerProvider Logs) : IDisposable
    {
        public IReadOnlyList<AuditEvent> Records => [.. Sink.Writes.Select(write => write.Event)];

        public void Dispose() => Services.Dispose();
    }

    private static ServiceCollection AuditingServices(RecordingLoggerProvider logs)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        return services;
    }

    private static Harness Build(ServiceCollection services, RecordingLoggerProvider logs, Exception? sinkFailure)
    {
        var provider = services.BuildServiceProvider();
        var sink = provider.GetRequiredService<RecordingAuditEventSink>();
        sink.ThrowOnWrite = sinkFailure;
        return new Harness(provider, sink, logs);
    }

    private static IEnumerable<string> DataValues(AuditEvent auditEvent)
        => (auditEvent.Data?.Values ?? []).SelectMany(value => value switch
        {
            null => [],
            string text => [text],
            IEnumerable items => items.Cast<object?>().Select(item => item?.ToString() ?? string.Empty),
            _ => [value.ToString() ?? string.Empty],
        });

    private static IEnumerable<string> RecordedValues(AuditEvent auditEvent)
        => new[] { auditEvent.ActorId, auditEvent.TargetId, auditEvent.Reason, auditEvent.OnBehalfOfId }
            .OfType<string>()
            .Concat(DataValues(auditEvent));

    private static bool Mentions(AuditEvent auditEvent, string value)
        => RecordedValues(auditEvent).Any(recorded => recorded == value);

    private static void AssertOneAuditWarning(Harness harness, string action)
        => Assert.Single(harness.Logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Text.Contains(action, StringComparison.Ordinal));

    private static async Task<Harness> StartKeyServiceAsync(HybridCache cache, int gracePeriodHours = 48, Exception? sinkFailure = null)
    {
        var logs = new RecordingLoggerProvider();
        var services = AuditingServices(logs);
        services.AddSingleton(cache);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["InternalToken:Issuer"] = "https://test-issuer",
            ["InternalToken:Audience"] = "forge",
            ["InternalToken:ExpirationMinutes"] = "60",
            ["InternalToken:KeyRotationIntervalHours"] = "24",
            ["InternalToken:KeyGracePeriodHours"] = gracePeriodHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["InternalToken:JwksCacheExpirationSeconds"] = "30",
            ["InternalToken:KeyAlgorithm"] = "ECDSA256",
        }).Build();
        services.AddInternalTokenFeature(configuration);

        var harness = Build(services, logs, sinkFailure);
        await harness.Services.GetRequiredService<KeyService>().StartAsync(CancellationToken.None);
        return harness;
    }

    private static async Task<IReadOnlyList<AuditEvent>> RotateAsync(Harness harness)
    {
        var before = harness.Records.Count;
        harness.Services.GetRequiredService<KeyService>().ManualRotate();

        var deadline = DateTime.UtcNow + RotationDeadline;
        while (DateTime.UtcNow < deadline)
        {
            var added = harness.Records.Skip(before).ToList();
            if (added.Any(record => record.Action == GatewayAuditActions.SigningKeyRotated))
            {
                return added;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"No {GatewayAuditActions.SigningKeyRotated} record within {RotationDeadline}");
        return [];
    }

    [Fact]
    public async Task Should_WriteOneGeneratedRecordNamingTheKeyId_When_TheKeyServiceStarts()
    {
        using var harness = await StartKeyServiceAsync(new ConcreteHybridCache());
        var kid = harness.Services.GetRequiredService<KeyService>().GetCurrentKeyId();

        var record = Assert.Single(harness.Records);
        Assert.Equal(GatewayAuditActions.SigningKeyGenerated, record.Action);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        Assert.True(Mentions(record, kid), $"The generated record does not name the key id {kid}");
    }

    [Fact]
    public async Task Should_StillStartWithAKeyAndLogOneWarning_When_TheGeneratedRecordCannotBeWritten()
    {
        using var harness = await StartKeyServiceAsync(new ConcreteHybridCache(), sinkFailure: new InvalidOperationException("sink down"));

        Assert.False(string.IsNullOrEmpty(harness.Services.GetRequiredService<KeyService>().GetCurrentKeyId()));
        AssertOneAuditWarning(harness, GatewayAuditActions.SigningKeyGenerated);
    }

    [Fact]
    public async Task Should_WriteOneRotatedRecordWithTheNewAndPrunedKeyIds_When_TheKeysRotate()
    {
        using var harness = await StartKeyServiceAsync(new ConcreteHybridCache(), gracePeriodHours: 0);
        var keyService = harness.Services.GetRequiredService<KeyService>();
        var prunedKid = keyService.GetCurrentKeyId();

        var added = await RotateAsync(harness);

        var record = Assert.Single(added);
        Assert.Equal(GatewayAuditActions.SigningKeyRotated, record.Action);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        var newKid = keyService.GetCurrentKeyId();
        Assert.NotEqual(prunedKid, newKid);
        Assert.True(Mentions(record, newKid), $"The rotated record does not name the new key id {newKid}");
        Assert.True(Mentions(record, prunedKid), $"The rotated record does not name the pruned key id {prunedKid}");
    }

    [Fact]
    public async Task Should_WriteOneRotatedFailureRecord_When_TheRotationThrowsACryptographicException()
    {
        var cache = new CryptoFailingHybridCache();
        using var harness = await StartKeyServiceAsync(cache);
        cache.FailWrites = true;

        var added = await RotateAsync(harness);

        var record = Assert.Single(added);
        Assert.Equal(GatewayAuditActions.SigningKeyRotated, record.Action);
        Assert.Equal(AuditOutcome.Failure, record.Outcome);
    }

    private static string Base64UrlSha256(string value)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string HexSha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static void AssertCarriesKeyTypeAndHashButNotTheKey(AuditEvent record, string presentedKey, EApiKeyType keyType)
    {
        Assert.Contains(keyType.ToString(), DataValues(record));
        Assert.Contains(DataValues(record), value => value == Base64UrlSha256(presentedKey) || value == HexSha256(presentedKey));
        Assert.DoesNotContain(RecordedValues(record), value => value.Contains(presentedKey, StringComparison.Ordinal));
    }

    private static Harness BuildApiKeyHarness(Action<Mock<IHashiCorpVaultClient>> arrangeVault, Exception? sinkFailure = null)
    {
        var logs = new RecordingLoggerProvider();
        var services = AuditingServices(logs);
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        services.Configure<ApiKeyAuthenticationOptions>(options => options.TimeoutSeconds = 5);

        var vault = new Mock<IHashiCorpVaultClient>();
        arrangeVault(vault);
        services.AddSingleton(vault.Object);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Constants.ApiKeySchemeId, _ => { });

        return Build(services, logs, sinkFailure);
    }

    private static void VaultHoldsTheServiceKey(Mock<IHashiCorpVaultClient> vault)
    {
        IReadOnlyCollection<ServiceAccessKeys> keys =
        [
            new ServiceAccessKeys { Name = ServiceName, Key = PrimaryKey, BackupKey = BackupKey, ServiceType = EServiceType.Service },
        ];
        vault.Setup(v => v.GetServiceAccessKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<ServiceAccessKeys>, Error>.Success(keys));
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(Harness harness, string scheme, string? headerName, string? headerValue)
    {
        var httpContext = new DefaultHttpContext { RequestServices = harness.Services };
        if (headerName is not null)
        {
            httpContext.Request.Headers[headerName] = headerValue;
        }

        harness.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;
        return await httpContext.AuthenticateAsync(scheme);
    }

    private static Task<AuthenticateResult> AuthenticateWithApiKeyAsync(Harness harness, string headerName, string headerValue)
        => AuthenticateAsync(harness, Constants.ApiKeySchemeId, headerName, headerValue);

    [Fact]
    public async Task Should_WriteNoRecord_When_ThePrimaryKeyAuthenticates()
    {
        using var harness = BuildApiKeyHarness(VaultHoldsTheServiceKey);

        var result = await AuthenticateWithApiKeyAsync(harness, "Authorization", $"Service {PrimaryKey}");

        Assert.True(result.Succeeded);
        Assert.Empty(harness.Records);
    }

    [Fact]
    public async Task Should_WriteOneSuccessRecordWithTheKeyTypeAndHash_When_TheBackupKeyAuthenticates()
    {
        using var harness = BuildApiKeyHarness(VaultHoldsTheServiceKey);

        var result = await AuthenticateWithApiKeyAsync(harness, "Authorization", $"Service {BackupKey}");

        Assert.True(result.Succeeded);
        var record = Assert.Single(harness.Records);
        Assert.Equal(GatewayAuditActions.ApiKeyAuthenticated, record.Action);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        AssertCarriesKeyTypeAndHashButNotTheKey(record, BackupKey, EApiKeyType.Service);
    }

    [Fact]
    public async Task Should_StillAuthenticateAndLogOneWarning_When_TheBackupKeyRecordCannotBeWritten()
    {
        using var harness = BuildApiKeyHarness(VaultHoldsTheServiceKey, new InvalidOperationException("sink down"));

        var result = await AuthenticateWithApiKeyAsync(harness, "Authorization", $"Service {BackupKey}");

        Assert.True(result.Succeeded);
        AssertOneAuditWarning(harness, GatewayAuditActions.ApiKeyAuthenticated);
    }

    [Theory]
    [InlineData("Authorization", $"Service {UnknownKey}", EApiKeyType.Service)]
    [InlineData("Authorization", $"Provider {UnknownKey}", EApiKeyType.Provider)]
    [InlineData("X-Api-ServiceToken", UnknownKey, EApiKeyType.LegacyService)]
    public async Task Should_WriteOneDeniedInvalidRecord_When_TheKeyMatchesNoVaultEntry(string headerName, string headerValue, EApiKeyType keyType)
    {
        using var harness = BuildApiKeyHarness(VaultHoldsTheServiceKey);

        var result = await AuthenticateWithApiKeyAsync(harness, headerName, headerValue);

        Assert.Equal("Invalid API key.", result.Failure?.Message);
        var record = Assert.Single(harness.Records);
        Assert.Equal(GatewayAuditActions.ApiKeyAuthenticated, record.Action);
        Assert.Equal(AuditOutcome.Denied, record.Outcome);
        Assert.Equal(GatewayAuditActions.DenialReasons.ApiKeyInvalid, record.Reason);
        AssertCarriesKeyTypeAndHashButNotTheKey(record, UnknownKey, keyType);
    }

    [Fact]
    public async Task Should_WriteOneDeniedTimedOutRecord_When_TheVaultLookupIsCancelled()
    {
        using var harness = BuildApiKeyHarness(vault => vault
            .Setup(v => v.GetServiceAccessKeysAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException()));

        var result = await AuthenticateWithApiKeyAsync(harness, "Authorization", $"Service {PrimaryKey}");

        Assert.Equal("API key validation timed out.", result.Failure?.Message);
        var record = Assert.Single(harness.Records);
        Assert.Equal(GatewayAuditActions.ApiKeyAuthenticated, record.Action);
        Assert.Equal(AuditOutcome.Denied, record.Outcome);
        Assert.Equal(GatewayAuditActions.DenialReasons.ApiKeyLookupTimedOut, record.Reason);
        AssertCarriesKeyTypeAndHashButNotTheKey(record, PrimaryKey, EApiKeyType.Service);
    }

    [Fact]
    public async Task Should_StillFailWithTheSameMessageAndLogOneWarning_When_TheDenialRecordCannotBeWritten()
    {
        using var harness = BuildApiKeyHarness(VaultHoldsTheServiceKey, new InvalidOperationException("sink down"));

        var result = await AuthenticateWithApiKeyAsync(harness, "Authorization", $"Service {UnknownKey}");

        Assert.Equal("Invalid API key.", result.Failure?.Message);
        AssertOneAuditWarning(harness, GatewayAuditActions.ApiKeyAuthenticated);
    }

    [Fact]
    public async Task Should_WriteNoRecord_When_NoApiKeyIsPresented()
    {
        using var harness = BuildApiKeyHarness(VaultHoldsTheServiceKey);

        var result = await AuthenticateAsync(harness, Constants.ApiKeySchemeId, headerName: null, headerValue: null);

        Assert.True(result.None);
        Assert.Empty(harness.Records);
    }

    private static Harness BuildDevOverrideHarness(bool enabled, Exception? sinkFailure = null)
    {
        var logs = new RecordingLoggerProvider();
        var services = AuditingServices(logs);
        var overrideOptions = new Mock<IOptionsMonitor<DevAuthOverrideOptions>>();
        overrideOptions.SetupGet(monitor => monitor.CurrentValue).Returns(new DevAuthOverrideOptions { Enabled = enabled });
        services.AddSingleton(overrideOptions.Object);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, DevOverrideAuthenticationHandler>(Constants.DevOverrideSchemeId, _ => { });
        return Build(services, logs, sinkFailure);
    }

    [Fact]
    public async Task Should_WriteOneSuccessRecordWithTheImpersonatedSubAsActor_When_TheOverrideAuthenticates()
    {
        using var harness = BuildDevOverrideHarness(enabled: true);

        var result = await AuthenticateAsync(harness, Constants.DevOverrideSchemeId, "Authorization", $"Bearer {ImpersonatedSub}");

        Assert.True(result.Succeeded);
        var record = Assert.Single(harness.Records);
        Assert.Equal(GatewayAuditActions.DevOverrideAuthenticated, record.Action);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        Assert.Equal(ImpersonatedSub, record.ActorId);
    }

    [Theory]
    [InlineData(false, $"Bearer {ImpersonatedSub}")]
    [InlineData(true, null)]
    [InlineData(true, "Bearer ")]
    public async Task Should_WriteNoRecord_When_TheOverrideDoesNotAuthenticate(bool enabled, string? authorization)
    {
        using var harness = BuildDevOverrideHarness(enabled);

        var result = await AuthenticateAsync(harness, Constants.DevOverrideSchemeId, authorization is null ? null : "Authorization", authorization);

        Assert.False(result.Succeeded);
        Assert.Empty(harness.Records);
    }

    [Fact]
    public async Task Should_StillAuthenticateAndLogOneWarning_When_TheImpersonationRecordCannotBeWritten()
    {
        using var harness = BuildDevOverrideHarness(enabled: true, new InvalidOperationException("sink down"));

        var result = await AuthenticateAsync(harness, Constants.DevOverrideSchemeId, "Authorization", $"Bearer {ImpersonatedSub}");

        Assert.True(result.Succeeded);
        AssertOneAuditWarning(harness, GatewayAuditActions.DevOverrideAuthenticated);
    }
}
