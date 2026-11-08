using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Userorg.Adapter;
using Userorg.Adapter.Waldur.Sync;

using Waldur.Contracts;
using Waldur.Sync;
using Waldur.Sync.Diff;
using Waldur.Sync.Worker;

namespace Waldur.Adapter.Tests;

/// <summary>
/// B12 wire-up acceptance: <see cref="WaldurSyncModuleExtensions.AddWaldurSyncModule"/> composes the
/// producer (one worker per enabled instance) + consumer (publisher, handlers, per-instance seams)
/// from the <c>Waldur:Instances</c> shape. Proves the DI graph resolves for 0, 1 and 2 enabled
/// instances (Aspire cannot boot inside a block worktree, so this stands in for "AppHost boots").
/// </summary>
public sealed class WaldurSyncModuleRegistrationTests
{
    private static Dictionary<string, string?> Instance(string prefix, string name, bool enabled) => new()
    {
        [$"{prefix}:Name"] = name,
        [$"{prefix}:Enabled"] = enabled ? "true" : "false",
        [$"{prefix}:BaseUrl"] = $"https://{name}.waldur.test/",
        [$"{prefix}:CustomersEndpoint"] = "/api/customers/",
        [$"{prefix}:ProjectsEndpoint"] = "/api/projects/",
        [$"{prefix}:MarketplacePublicOfferingsEndpoint"] = "/api/marketplace-public-offerings/",
        [$"{prefix}:MarketplaceResourcesEndpoint"] = "/api/marketplace-resources/",
        [$"{prefix}:AuthorizationHeader"] = "Authorization",
        [$"{prefix}:AuthorizationHeaderValuePrefix"] = "Token",
        [$"{prefix}:EndpointApiKey"] = "secret",
        [$"{prefix}:Identity:ServiceName"] = $"{name}-Waldur",
        [$"{prefix}:Identity:ServiceAccountUserName"] = $"{name}-svc",
        [$"{prefix}:Identity:WaldurPreferredAuthType"] = "SshCertificate",
        [$"{prefix}:Identity:WaldurPIEmail"] = $"pi@{name}.test"
    };

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWaldurSyncModule(configuration);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Merge(params Dictionary<string, string?>[] parts)
    {
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            foreach (var kv in part)
            {
                merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }

    [Fact(DisplayName = "0 enabled instances registers no workers, publisher or consumer options")]
    public void Should_RegisterNothing_When_NoEnabledInstances()
    {
        using var sp = Build(Instance("Waldur:Instances:0", "EFP", enabled: false));

        Assert.Empty(sp.GetServices<IHostedService>().OfType<WaldurSyncWorker>());
        Assert.Null(sp.GetService<IWaldurEventPublisher>());
        Assert.Null(sp.GetService<IWaldurConsumerOptions>());
    }

    [Fact(DisplayName = "1 enabled instance resolves 1 worker + publisher + per-instance options")]
    public void Should_ResolveGraph_When_OneEnabledInstance()
    {
        using var sp = Build(Instance("Waldur:Instances:0", "EFP", enabled: true));

        Assert.Single(sp.GetServices<IHostedService>().OfType<WaldurSyncWorker>());
        Assert.NotNull(sp.GetRequiredService<IWaldurEventPublisher>());
        Assert.NotNull(sp.GetRequiredService<IShaMapRebuilder>());

        var options = sp.GetRequiredService<IWaldurConsumerOptions>().For("EFP-Waldur");
        Assert.Equal("EFP-Waldur", options.ServiceName);
        Assert.Equal("SshCertificate", options.WaldurPreferredAuthType);
        Assert.Equal("pi@EFP.test", options.WaldurPiEmail);
        Assert.Equal("External", options.ServiceProjectDomainName);
    }

    [Fact(DisplayName = "2 enabled instances resolve 2 workers + both per-instance option sets")]
    public void Should_ResolveGraph_When_TwoEnabledInstances()
    {
        using var sp = Build(Merge(
            Instance("Waldur:Instances:0", "EFP", enabled: true),
            Instance("Waldur:Instances:1", "Puhuri", enabled: true)));

        Assert.Equal(2, sp.GetServices<IHostedService>().OfType<WaldurSyncWorker>().Count());

        var consumerOptions = sp.GetRequiredService<IWaldurConsumerOptions>();
        Assert.Equal("EFP-Waldur", consumerOptions.For("EFP-Waldur").ServiceName);
        Assert.Equal("Puhuri-Waldur", consumerOptions.For("Puhuri-Waldur").ServiceName);

        Assert.NotNull(sp.GetRequiredKeyedService<IWaldurApiClient>("EFP"));
        Assert.NotNull(sp.GetRequiredKeyedService<IWaldurApiClient>("Puhuri"));
    }

    [Fact(DisplayName = "Disabled instance among enabled ones gets no worker")]
    public void Should_SkipDisabledInstance_When_Mixed()
    {
        using var sp = Build(Merge(
            Instance("Waldur:Instances:0", "EFP", enabled: true),
            Instance("Waldur:Instances:1", "Puhuri", enabled: false)));

        Assert.Single(sp.GetServices<IHostedService>().OfType<WaldurSyncWorker>());
        Assert.NotNull(sp.GetRequiredService<IWaldurConsumerOptions>().For("EFP-Waldur"));
    }
}
