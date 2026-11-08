using System.Diagnostics;
using System.Security.Claims;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Configuration;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes how <c>ApiKeyAuthenticationHandler</c> finds the key and looks it up: a
/// <c>Service</c>/<c>Provider</c> Authorization scheme wins, any other non-blank Authorization value means
/// "not an API key", a blank one falls back to the legacy headers in the order X-Api-ServiceToken,
/// X-Api-ProviderServiceKey, X-Api-Token; the vault list is cached, matched on the primary or backup key,
/// and every outcome leaves its log line and vault.lookup activity status.
/// </summary>
public sealed class ApiKeyAuthenticationHandlerTests
{
    private const string ServiceKey = "service-key-0001";
    private const string ProviderKey = "provider-key-0002";
    private const string DdiKey = "ddi-key-0003";
    private const string ServiceBackupKey = "service-backup-0004";
    private const string UnknownKey = "unknown-key-0005";
    private const string ServiceName = "svc-a";
    private const string ProviderName = "provider-b";
    private const string DdiName = "ddi-c";

    private static readonly IReadOnlyCollection<ServiceAccessKeys> VaultKeys =
    [
        new ServiceAccessKeys { Name = ServiceName, Key = ServiceKey, BackupKey = ServiceBackupKey, ServiceType = EServiceType.Service },
        new ServiceAccessKeys { Name = ProviderName, Key = ProviderKey, BackupKey = "provider-backup", ServiceType = EServiceType.Provider, Data = "provider-id-1" },
        new ServiceAccessKeys { Name = DdiName, Key = DdiKey, BackupKey = "ddi-backup", ServiceType = EServiceType.DDI },
    ];

    [Theory]
    [InlineData("Bearer some-token")]
    [InlineData("ServiceKeyWithoutSpace")]
    [InlineData(" Service leading-space")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task Should_ReturnNoResultWithoutAVaultCall_When_TheAuthorizationIsNotAnApiKeyScheme(string authorization)
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("Authorization", authorization), ("X-Api-ServiceToken", ServiceKey));

        Assert.True(result.None);
        harness.Vault.Verify(v => v.GetServiceAccessKeysAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Service", ServiceKey, nameof(EApiKeyType.Service), ServiceName)]
    [InlineData("SERVICE", ServiceKey, nameof(EApiKeyType.Service), ServiceName)]
    [InlineData("Provider", ProviderKey, nameof(EApiKeyType.Provider), "provider-id-1")]
    [InlineData("provider", ProviderKey, nameof(EApiKeyType.Provider), "provider-id-1")]
    public async Task Should_AuthenticateWithTheSchemeAsRole_When_TheAuthorizationNamesAnApiKeyScheme(string scheme, string key, string role, string subject)
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("Authorization", $"{scheme} {key}"));

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(role, result.Principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(subject, result.Principal.FindFirstValue("sub"));
    }

    [Theory]
    [InlineData("X-Api-ServiceToken", ServiceKey, nameof(EApiKeyType.LegacyService), ServiceName)]
    [InlineData("X-Api-ProviderServiceKey", ProviderKey, nameof(EApiKeyType.LegacyProvider), ProviderName)]
    [InlineData("X-Api-Token", DdiKey, nameof(EApiKeyType.LegacyService), DdiName)]
    public async Task Should_FallBackToTheLegacyHeader_When_TheAuthorizationIsBlank(string header, string key, string role, string serviceName)
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("Authorization", " "), (header, key));

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(role, result.Principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal(serviceName, result.Principal.FindFirstValue("preferred_username"));
    }

    [Fact]
    public async Task Should_PreferTheServiceTokenHeader_When_EveryLegacyHeaderIsPresent()
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("X-Api-Token", DdiKey), ("X-Api-ProviderServiceKey", ProviderKey), ("X-Api-ServiceToken", ServiceKey));

        Assert.Equal(ServiceName, result.Principal!.FindFirstValue("preferred_username"));
    }

    [Fact]
    public async Task Should_PreferTheProviderServiceKeyHeader_When_TheServiceTokenHeaderIsBlank()
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("X-Api-Token", DdiKey), ("X-Api-ProviderServiceKey", ProviderKey), ("X-Api-ServiceToken", " "));

        Assert.Equal(ProviderName, result.Principal!.FindFirstValue("preferred_username"));
    }

    [Fact]
    public async Task Should_ReturnNoResult_When_EveryLegacyHeaderIsBlank()
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("X-Api-Token", " "), ("X-Api-ProviderServiceKey", ""), ("X-Api-ServiceToken", " "));

        Assert.True(result.None);
    }

    [Theory]
    [InlineData("Authorization", $"Provider {ProviderKey}", "Authorization", nameof(EApiKeyType.Provider))]
    [InlineData("X-Api-ProviderServiceKey", ProviderKey, "X-Api-ProviderServiceKey", nameof(EApiKeyType.LegacyProvider))]
    [InlineData("X-Api-Token", DdiKey, "X-Api-Token", nameof(EApiKeyType.LegacyService))]
    [InlineData("Authorization", "Bearer token", "None", nameof(EApiKeyType.Unknown))]
    public async Task Should_TagTheCurrentActivityWithTheHeaderSourceAndKeyType_When_AuthenticationRuns(string header, string value, string headerSource, string keyType)
    {
        await using var harness = new Harness();
        using var current = new Activity("request");
        current.Start();

        await harness.AuthenticateAsync((header, value));

        Assert.Equal(headerSource, current.GetTagItem("auth.header_source"));
        Assert.Equal(keyType, current.GetTagItem("auth.key_type"));
    }

    [Fact]
    public async Task Should_AuthenticateWithTheBackupKey_When_ThePrimaryKeyDoesNotMatch()
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("Authorization", $"Service {ServiceBackupKey}"));

        Assert.True(result.Succeeded, result.Failure?.Message);
        Assert.Equal(ServiceName, result.Principal.FindFirstValue("sub"));
    }

    [Fact]
    public async Task Should_ReadTheVaultOnce_When_KeysAreLookedUpRepeatedly()
    {
        await using var harness = new Harness();

        await harness.AuthenticateAsync(("Authorization", $"Service {ServiceKey}"));
        var second = await harness.AuthenticateAsync(("Authorization", $"Provider {ProviderKey}"));

        Assert.True(second.Succeeded);
        harness.Vault.Verify(v => v.GetServiceAccessKeysAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_FailAsInvalidAndLogAnError_When_TheVaultCannotBeRead()
    {
        await using var harness = new Harness(Result<IReadOnlyCollection<ServiceAccessKeys>, Error>.Failure(new Error("vault.down", ErrorSeverity.Error, "vault sealed")));

        var result = await harness.AuthenticateAsync(("Authorization", $"Service {ServiceKey}"));

        Assert.Equal("Invalid API key.", result.Failure?.Message);
        var error = Assert.Single(harness.Logs.All, entry => entry.Level == LogLevel.Error);
        Assert.StartsWith("Failed to retrieve service access keys from Vault: ", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_LogAWarningWithTheMaskedHash_When_TheKeyMatchesNoVaultEntry()
    {
        await using var harness = new Harness();

        var result = await harness.AuthenticateAsync(("Authorization", $"Service {UnknownKey}"));

        Assert.Equal("Invalid API key.", result.Failure?.Message);
        var warning = Assert.Single(harness.Logs.All, entry => entry.Level == LogLevel.Warning);
        Assert.Matches(@"^No service access key found in Vault for apiKey hash [A-Za-z0-9_-]{8}\.\.\.$", warning.Message);
    }

    [Fact]
    public async Task Should_LogTheCancellationAndTheTimeout_When_TheVaultLookupIsCancelled()
    {
        await using var harness = new Harness(failure: new OperationCanceledException());

        var result = await harness.AuthenticateAsync(("Authorization", $"Service {ServiceKey}"));

        Assert.Equal("API key validation timed out.", result.Failure?.Message);
        var warnings = harness.Logs.All.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.StartsWith("Vault GetServiceAccessKeysAsync was canceled after ", warnings[0], StringComparison.Ordinal);
        Assert.Equal("API key vault lookup timed out after 5s for keyType Service", warnings[1]);
    }

    [Fact]
    public async Task Should_LogTheFoundServiceAtInformation_When_TheKeyMatches()
    {
        await using var harness = new Harness();

        await harness.AuthenticateAsync(("Authorization", $"Service {ServiceKey}"));

        var found = Assert.Single(harness.Logs.All, entry => entry.Level == LogLevel.Information);
        Assert.Equal($"Found vault entry for service {ServiceName}", found.Message);
    }

    [Fact]
    public async Task Should_Throw_When_AProviderEntryHasNoProviderData()
    {
        IReadOnlyCollection<ServiceAccessKeys> keys =
        [
            new ServiceAccessKeys { Name = ProviderName, Key = ProviderKey, BackupKey = "provider-backup", ServiceType = EServiceType.Provider, Data = null },
        ];
        await using var harness = new Harness(Result<IReadOnlyCollection<ServiceAccessKeys>, Error>.Success(keys));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.AuthenticateAsync(("Authorization", $"Provider {ProviderKey}")));

        Assert.Equal("Provider data is missing", exception.Message);
    }

    [Theory]
    [InlineData($"Service {ServiceKey}", "vault.result.direct_key")]
    [InlineData($"Provider {ProviderKey}", "vault.result.provider_data")]
    [InlineData($"Service {DdiKey}", "vault.result.vault_path")]
    public async Task Should_EndTheLookupActivityOkWithTheResultKind_When_TheKeyMatches(string authorization, string resultEvent)
    {
        await using var harness = new Harness();
        using var testRoot = new Activity("test.root");
        testRoot.Start();
        List<Activity> recorded = [];
        using var listener = ListenToApiKeyActivities(testRoot.TraceId, recorded);

        await harness.AuthenticateAsync(("Authorization", authorization));

        var lookup = Assert.Single(recorded, activity => activity.OperationName == "vault.lookup");
        Assert.Equal(ActivityStatusCode.Ok, lookup.Status);
        Assert.Equal(
            ["vault.lookup.started", "vault.cache.miss", "vault.keys.ready", "vault.lookup.hit", resultEvent],
            lookup.Events.Select(e => e.Name));
    }

    [Fact]
    public async Task Should_EndTheLookupActivityInError_When_TheKeyMatchesNoVaultEntry()
    {
        await using var harness = new Harness();
        using var testRoot = new Activity("test.root");
        testRoot.Start();
        List<Activity> recorded = [];
        using var listener = ListenToApiKeyActivities(testRoot.TraceId, recorded);

        await harness.AuthenticateAsync(("Authorization", $"Service {UnknownKey}"));

        var lookup = Assert.Single(recorded, activity => activity.OperationName == "vault.lookup");
        Assert.Equal(ActivityStatusCode.Error, lookup.Status);
        Assert.Equal("API key not found in Vault", lookup.StatusDescription);
        Assert.Equal(nameof(EApiKeyType.Service), lookup.GetTagItem("auth.key_type"));
        Assert.Equal("vault.lookup.missed", lookup.Events.Last().Name);
    }

    [Fact]
    public async Task Should_EndTheLookupActivityInError_When_TheVaultCannotBeRead()
    {
        await using var harness = new Harness(Result<IReadOnlyCollection<ServiceAccessKeys>, Error>.Failure(new Error("vault.down", ErrorSeverity.Error, "vault sealed")));
        using var testRoot = new Activity("test.root");
        testRoot.Start();
        List<Activity> recorded = [];
        using var listener = ListenToApiKeyActivities(testRoot.TraceId, recorded);

        await harness.AuthenticateAsync(("Authorization", $"Service {ServiceKey}"));

        var lookup = Assert.Single(recorded, activity => activity.OperationName == "vault.lookup");
        Assert.Equal(ActivityStatusCode.Error, lookup.Status);
        Assert.Contains(lookup.Events, e => e.Name == "vault.lookup.error" && Equals(e.Tags.Single().Value, "vault sealed"));
    }

    /// <summary>
    /// <see cref="ActivitySource.AddActivityListener"/> registers process-wide and other test classes run the
    /// same handler in parallel, so only activities under this test's own root trace are recorded.
    /// </summary>
    private static ActivityListener ListenToApiKeyActivities(ActivityTraceId traceId, List<Activity> recorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "UserOrg.Gateway.ApiKeyAuth",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    recorded.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        public Harness(Result<IReadOnlyCollection<ServiceAccessKeys>, Error>? vaultResult = null, Exception? failure = null)
        {
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
            services.AddHttpContextAccessor();
            services.AddSingleton<RecordingAuditEventSink>();
            services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
            services.AddDistributedMemoryCache();
            services.AddHybridCache();
            services.Configure<ApiKeyAuthenticationOptions>(options => options.TimeoutSeconds = 5);

            var setup = Vault.Setup(v => v.GetServiceAccessKeysAsync(It.IsAny<CancellationToken>()));
            if (failure is not null)
            {
                setup.ThrowsAsync(failure);
            }
            else
            {
                setup.ReturnsAsync(vaultResult ?? Result<IReadOnlyCollection<ServiceAccessKeys>, Error>.Success(VaultKeys));
            }

            services.AddSingleton(Vault.Object);
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Constants.ApiKeySchemeId, _ => { });
            _services = services.BuildServiceProvider();
        }

        public Mock<IHashiCorpVaultClient> Vault { get; } = new();

        public CapturingLoggerProvider Logs { get; } = new();

        public Task<AuthenticateResult> AuthenticateAsync(params (string Name, string Value)[] headers)
        {
            var httpContext = new DefaultHttpContext { RequestServices = _services };
            foreach (var (name, value) in headers)
            {
                httpContext.Request.Headers[name] = value;
            }

            return httpContext.AuthenticateAsync(Constants.ApiKeySchemeId);
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            Logs.Dispose();
        }
    }
}
