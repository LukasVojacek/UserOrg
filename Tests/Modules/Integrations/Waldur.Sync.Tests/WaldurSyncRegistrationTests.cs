using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Waldur.Sync.Configuration;

using Xunit;

namespace Waldur.Sync.Tests;

public class WaldurSyncRegistrationTests
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

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Should_RegisterNothing_When_NoEnabledInstances()
    {
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: false);
        var services = new ServiceCollection();

        services.AddWaldurSync(Config(values));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IWaldurApiClient));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IValidateOptions<WaldurSyncOptions>));
    }

    [Fact]
    public void Should_RegisterNothing_When_SectionAbsent()
    {
        var services = new ServiceCollection();

        services.AddWaldurSync(Config([]));

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IWaldurApiClient));
    }

    [Fact]
    public void Should_RegisterKeyedClient_When_InstanceEnabled()
    {
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: true);
        var services = new ServiceCollection();

        services.AddWaldurSync(Config(values));

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredKeyedService<IWaldurApiClient>("EFP");
        Assert.NotNull(client);
    }

    [Fact]
    public void Should_BindAllEnabledInstances_When_Configured()
    {
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: true);
        foreach (var kv in ValidInstance("Waldur:Instances:1", "Puhuri", enabled: true))
        {
            values[kv.Key] = kv.Value;
        }

        var services = new ServiceCollection();
        services.AddWaldurSync(Config(values));

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<WaldurSyncOptions>>().Value;

        Assert.Equal(2, options.EnabledInstances.Count());
        Assert.NotNull(sp.GetRequiredKeyedService<IWaldurApiClient>("EFP"));
        Assert.NotNull(sp.GetRequiredKeyedService<IWaldurApiClient>("Puhuri"));
    }

    [Fact]
    public void Should_FailValidation_When_EnabledInstanceMissingApiKey()
    {
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: true);
        values.Remove("Waldur:Instances:0:EndpointApiKey");

        var services = new ServiceCollection();
        services.AddWaldurSync(Config(values));

        using var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<OptionsValidationException>(
            () => _ = sp.GetRequiredService<IOptions<WaldurSyncOptions>>().Value);
        Assert.Contains("EndpointApiKey", string.Join(";", ex.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_FailValidation_When_EnabledInstanceMissingServiceName()
    {
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: true);
        values.Remove("Waldur:Instances:0:Identity:ServiceName");

        var services = new ServiceCollection();
        services.AddWaldurSync(Config(values));

        using var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<OptionsValidationException>(
            () => _ = sp.GetRequiredService<IOptions<WaldurSyncOptions>>().Value);
        Assert.Contains("ServiceName", string.Join(";", ex.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotValidateDisabledInstances_When_Mixed()
    {
        // One enabled valid + one disabled invalid (missing everything) → valid overall.
        var values = ValidInstance("Waldur:Instances:0", "EFP", enabled: true);
        values["Waldur:Instances:1:Name"] = "Broken";
        values["Waldur:Instances:1:Enabled"] = "false";

        var services = new ServiceCollection();
        services.AddWaldurSync(Config(values));

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<WaldurSyncOptions>>().Value;
        Assert.Single(options.EnabledInstances);
    }
}
