using Waldur.Sync.Configuration;

using Xunit;

namespace Waldur.Sync.Tests;

public class WaldurSyncOptionsValidatorTests
{
    private static WaldurInstanceOptions ValidInstance() => new()
    {
        Name = "EFP",
        Enabled = true,
        BaseUrl = new Uri("https://waldur.test/"),
        CustomersEndpoint = new Uri("/api/customers/", UriKind.Relative),
        ProjectsEndpoint = new Uri("/api/projects/", UriKind.Relative),
        MarketplacePublicOfferingsEndpoint = new Uri("/api/marketplace-public-offerings/", UriKind.Relative),
        MarketplaceResourcesEndpoint = new Uri("/api/marketplace-resources/", UriKind.Relative),
        AuthorizationHeader = "Authorization",
        EndpointApiKey = "secret",
        Identity = new WaldurInstanceIdentityOptions
        {
            ServiceName = "EFP-Waldur",
            ServiceAccountUserName = "efp-svc",
            WaldurPIEmail = "pi@efp.test"
        }
    };

    private static WaldurSyncOptions Options(WaldurInstanceOptions instance)
    {
        var options = new WaldurSyncOptions();
        options.Instances.Add(instance);
        return options;
    }

    [Fact]
    public void Should_Pass_When_EndpointsAreRelative()
    {
        var result = new WaldurSyncOptionsValidator().Validate(null, Options(ValidInstance()));

        Assert.False(result.Failed);
    }

    [Fact]
    public void Should_Fail_When_EndpointIsAbsolute()
    {
        var instance = ValidInstance();
        instance.CustomersEndpoint = new Uri("https://elsewhere.test/api/customers/");

        var result = new WaldurSyncOptionsValidator().Validate(null, Options(instance));

        Assert.True(result.Failed);
        Assert.Contains(nameof(instance.CustomersEndpoint), result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("relative", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Should_Fail_When_EnabledInstanceMissingWaldurPIEmail()
    {
        var instance = ValidInstance();
        instance.Identity.WaldurPIEmail = null;

        var result = new WaldurSyncOptionsValidator().Validate(null, Options(instance));

        Assert.True(result.Failed);
        Assert.Contains("Identity.WaldurPIEmail", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotCheckRelativity_When_InstanceDisabled()
    {
        var instance = ValidInstance();
        instance.Enabled = false;
        instance.ProjectsEndpoint = new Uri("https://elsewhere.test/api/projects/");

        var result = new WaldurSyncOptionsValidator().Validate(null, Options(instance));

        Assert.False(result.Failed);
    }
}
