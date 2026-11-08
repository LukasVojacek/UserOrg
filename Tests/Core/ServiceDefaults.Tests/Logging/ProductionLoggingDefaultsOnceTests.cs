using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

using UserOrg.ServiceDefaults.Logging;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// The API adds the production logging defaults before its Vault source, so AddServiceDefaults must not insert
/// them a second time: every insert rebuilds the providers already added, and Vault would be fetched again.
/// </summary>
public sealed class ProductionLoggingDefaultsOnceTests
{
    [Fact(DisplayName = "Should_InsertTheDefaultsOnce_When_CalledTwice")]
    public void Should_InsertTheDefaultsOnce_When_CalledTwice()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddProductionLoggingDefaults();
        var countingSource = new CountingSource();
        ((IConfigurationBuilder)builder.Configuration).Add(countingSource);
        var buildsBefore = countingSource.Builds;
        builder.AddProductionLoggingDefaults();

        var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
        Assert.Single(sources.OfType<MemoryConfigurationSource>(), source => ReferenceEquals(source.InitialData, ProductionLoggingDefaults.Values));
        Assert.Same(ProductionLoggingDefaults.Values, ((MemoryConfigurationSource)sources[0]).InitialData);
        Assert.Equal(buildsBefore, countingSource.Builds);
    }

    private sealed class CountingSource : IConfigurationSource
    {
        public int Builds { get; private set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            Builds++;
            return new MemoryConfigurationProvider(new MemoryConfigurationSource());
        }
    }
}
