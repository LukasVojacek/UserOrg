using System.Net.Http.Headers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly;

using Waldur.Contracts;
using Waldur.Sync.Configuration;
using Waldur.Sync.Diff;
using Waldur.Sync.Http;
using Waldur.Sync.Mapping;
using Waldur.Sync.Worker;

namespace Waldur.Sync;

/// <summary>
/// Composition root for the Waldur.Sync module: binds and validates <c>Waldur:Instances</c> and
/// registers one typed, rate-limited, retrying <see cref="IWaldurApiClient"/> per enabled instance
/// (keyed by instance name). Zero enabled instances registers nothing.
/// </summary>
public static class WaldurSyncServiceCollectionExtensions
{

    /// <summary>The named-<see cref="HttpClient"/> registration key for an instance.</summary>
    internal static string HttpClientName(string instanceName) => $"Waldur:{instanceName}";

    /// <summary>
    /// Registers Waldur.Sync API clients for every enabled instance in <paramref name="configuration"/>.
    /// If no instance is enabled, nothing is registered.
    /// </summary>
    public static IServiceCollection AddWaldurSync(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(WaldurSyncOptions.SectionName);
        var enabledInstances = (section.Get<WaldurSyncOptions>() ?? new WaldurSyncOptions())
            .EnabledInstances.ToList();

        // Zero enabled instances → register nothing (avoids validate-on-start failures for an
        // absent/incomplete section, mirrors the legacy Enabled-gate).
        if (enabledInstances.Count == 0)
        {
            return services;
        }

        services.AddOptionsWithValidateOnStart<WaldurSyncOptions>().Bind(section);
        services.AddSingleton<IValidateOptions<WaldurSyncOptions>, WaldurSyncOptionsValidator>();

        foreach (var instance in enabledInstances)
        {
            RegisterInstance(services, instance);
        }

        return services;
    }

    private static void RegisterInstance(IServiceCollection services, WaldurInstanceOptions instance)
    {
        var clientName = HttpClientName(instance.Name);
        var httpBuilder = services.AddHttpClient(clientName, client =>
        {
            if (instance.BaseUrl is not null)
            {
                client.BaseAddress = instance.BaseUrl;
            }

            var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "0.0.0.1";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("lexis-userorg", version));

            if (!string.IsNullOrWhiteSpace(instance.AuthorizationHeader) &&
                !string.IsNullOrWhiteSpace(instance.EndpointApiKey))
            {
                var headerValue = $"{instance.AuthorizationHeaderValuePrefix} {instance.EndpointApiKey}".Trim();
                client.DefaultRequestHeaders.Add(instance.AuthorizationHeader, headerValue);
            }

            if (instance.Timeout is not null)
            {
                client.Timeout = instance.Timeout.Value;
            }
        });

        // ServiceDefaults applies a global AddStandardResilienceHandler to EVERY client (30s total /
        // ~10s attempt timeout / ~3 retries + circuit breaker). Named-client handlers STACK on top of
        // it, nesting retries (~6-8 physical attempts) and capping our unbounded rate-limit queue wait
        // with a timeout this client never chose. Strip it first, then install ONLY our own resilience.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is [Experimental] but is the documented way to drop ServiceDefaults' stacked global handler for this named client.
        httpBuilder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        // Retry is registered OUTER, rate-limit INNER: every retried attempt re-enters the throttle,
        // so retries never bypass the per-second limit (no retry storm defeating the limiter).
        httpBuilder.AddResilienceHandler($"waldur-{instance.Name}-retry",
            builder => builder.AddRetry(WaldurRetryStrategy.Build(instance)));
        httpBuilder.AddHttpMessageHandler(() => new RateLimitHandler(instance.RateLimitPerSecond, instance.RateLimitBurst));

        services.AddKeyedTransient<IWaldurApiClient>(instance.Name, (sp, key) =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
            var options = sp.GetRequiredService<IOptions<WaldurSyncOptions>>().Value
                .Instances.First(i => string.Equals(i.Name, (string)key!, StringComparison.Ordinal));
            return new WaldurApiClient(httpClient, options);
        });

        // One sequential BackgroundService per enabled instance (ADR-0056).
        services.AddSingleton<IHostedService>(sp =>
        {
            var client = sp.GetRequiredKeyedService<IWaldurApiClient>(instance.Name);
            return new WaldurSyncWorker(
                instance,
                client,
                new WaldurMapper(instance, sp.GetService<TimeProvider>() ?? TimeProvider.System),
                new DiffEngine(instance.Name, instance.Identity.ServiceName!),
                sp.GetRequiredService<IWaldurEventPublisher>(),
                sp.GetRequiredService<IShaMapRebuilder>(),
                sp.GetRequiredService<ILogger<WaldurSyncWorker>>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System);
        });
    }
}