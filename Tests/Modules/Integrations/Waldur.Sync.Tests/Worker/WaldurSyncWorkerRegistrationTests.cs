using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Waldur.Contracts;
using Waldur.Contracts.Events;
using Waldur.Sync.Diff;
using Waldur.Sync.Worker;

using Xunit;

namespace Waldur.Sync.Tests.Worker;

public class WaldurSyncWorkerRegistrationTests
{
    private static Dictionary<string, string?> ValidInstance(string prefix, string name, bool enabled) => new()
    {
        [$"{prefix}:Name"] = name,
        [$"{prefix}:Enabled"] = enabled ? "true" : "false",
        [$"{prefix}:BaseUrl"] = "https://waldur.test/",
        [$"{prefix}:CustomersEndpoint"] = "/api/customers/",
        [$"{prefix}:ProjectsEndpoint"] = "/api/projects/",
        [$"{prefix}:MarketplacePublicOfferingsEndpoint"] = "/api/marketplace-public-offerings/",
        [$"{prefix}:MarketplaceResourcesEndpoint"] = "/api/marketplace-resources/",
        [$"{prefix}:AuthorizationHeader"] = "Authorization",
        [$"{prefix}:AuthorizationHeaderValuePrefix"] = "Token",
        [$"{prefix}:EndpointApiKey"] = "secret",
        [$"{prefix}:Identity:ServiceName"] = $"{name}-Waldur",
        [$"{prefix}:Identity:ServiceAccountUserName"] = $"{name}-svc",
        [$"{prefix}:Identity:WaldurPIEmail"] = $"pi@{name}.test"
    };

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IWaldurEventPublisher, NoopPublisher>();
        services.AddSingleton<IShaMapRebuilder, EmptyRebuilder>();
        services.AddWaldurSync(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Should_RegisterOneWorkerPerEnabledInstance()
    {
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: true);
        foreach (var kv in ValidInstance("Waldur:Instances:1", "Puhuri", enabled: true))
        {
            values[kv.Key] = kv.Value;
        }

        using var sp = BuildProvider(values);

        var workers = sp.GetServices<IHostedService>().OfType<WaldurSyncWorker>().ToList();
        Assert.Equal(2, workers.Count);
    }

    [Fact]
    public void Should_RegisterNoWorker_When_NoEnabledInstance()
    {
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: false);

        using var sp = BuildProvider(values);

        Assert.Empty(sp.GetServices<IHostedService>().OfType<WaldurSyncWorker>());
    }

    private sealed class NoopPublisher : IWaldurEventPublisher
    {
        public Task<Result<Error>> PublishAsync(WaldurIntegrationEvent evt, CancellationToken ct) =>
            Task.FromResult(Result<Error>.Success());
    }

    private sealed class EmptyRebuilder : IShaMapRebuilder
    {
        public Task<IReadOnlyDictionary<ShaKey, string>> RebuildAsync(string instance, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<ShaKey, string>>(new Dictionary<ShaKey, string>());
    }
}
