using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.InternalToken;

namespace UserOrg.Gateway.Tests;

public sealed class InternalTokenFeatureExtensionsTests
{

    [Fact]

    public void AddInternalTokenFeature_RegistersExpectedServices()

    {

        // Arrange

        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();



        // Act

        var returned = services.AddInternalTokenFeature(configuration);



        // Assert - registration descriptors exist for required types

        Assert.Same(services, returned);

        Assert.Contains(services, sd => sd.ServiceType == typeof(KeyService));

        Assert.Contains(services, sd => sd.ServiceType == typeof(TokenService));

        Assert.Contains(services, sd => sd.ServiceType == typeof(IInternalTokenProvider));

        Assert.Contains(services, sd => sd.ServiceType == typeof(IJwksProvider));

    }



    [Fact]

    public void InternalTokenProvider_Constructor_ThrowsOnNullArguments()

    {

        Assert.Throws<System.ArgumentNullException>(() => new InternalTokenProvider(null!, null!, null!, null!));

    }

}