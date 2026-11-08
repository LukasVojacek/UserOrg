using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.InternalToken;

namespace UserOrg.Gateway.Tests;

internal static class InternalTokenTestServices
{
    /// <summary>
    /// Builds a container whose <see cref="KeyService"/> has already produced its initial signing key.
    /// Disposing the returned provider disposes <see cref="KeyService"/>, which stops the rotation timer.
    /// </summary>
    public static async Task<ServiceProvider> BuildStartedAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));

        // The real HybridCache, not a double: the cache key semantics are exactly what these tests
        // characterize, so a hand-rolled cache would only pin the double's behaviour.
        services.AddDistributedMemoryCache();
        services.AddHybridCache();

        var inMemory = new Dictionary<string, string?>
        {
            ["InternalToken:Issuer"] = "https://test-issuer",
            ["InternalToken:Audience"] = "forge",
            ["InternalToken:ExpirationMinutes"] = "60",
            ["InternalToken:KeyRotationIntervalHours"] = "24",
            ["InternalToken:KeyGracePeriodHours"] = "48",
            ["InternalToken:JwksCacheExpirationSeconds"] = "30",
            ["InternalToken:KeyAlgorithm"] = "ECDSA512",
            ["InternalToken:TokenCacheSeconds"] = "300"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        services.AddInternalTokenFeature(configuration);

        var serviceProvider = services.BuildServiceProvider();
        await serviceProvider.GetRequiredService<KeyService>().StartAsync(CancellationToken.None);
        return serviceProvider;
    }
}
