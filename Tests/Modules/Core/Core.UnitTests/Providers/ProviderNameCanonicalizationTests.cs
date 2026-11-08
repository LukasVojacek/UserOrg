using DomainProviderName = Core.Domain.Provider.ProviderName;
using DomainLocationName = Core.Domain.Provider.LocationName;

namespace Core.UnitTests.Providers;

public sealed class ProviderNameCanonicalizationTests
{
    [Theory]
    [InlineData("AcmeHPC", "AcmeHPC")]
    [InlineData("  Acme HPC  ", "Acme HPC")]
    [InlineData("acme", "acme")]
    public void Should_PreserveProviderNameCasing_But_TrimWhitespace(string input, string expected)
    {
        Assert.Equal(expected, DomainProviderName.Create(input).Value);
        Assert.Equal(expected, DomainProviderName.From(input).Value);
    }

    [Fact]
    public void Should_TreatProviderNamesOfDifferentCasing_As_Equal()
    {
        Assert.Equal(DomainProviderName.Create("AcmeHPC"), DomainProviderName.From("acmehpc"));
        Assert.Equal(DomainProviderName.Create("AcmeHPC").GetHashCode(), DomainProviderName.From("acmehpc").GetHashCode());
    }

    [Theory]
    [InlineData("GPU", "GPU")]
    [InlineData("  Gpu Node ", "Gpu Node")]
    public void Should_PreserveLocationNameCasing_But_TrimWhitespace(string input, string expected)
    {
        Assert.Equal(expected, DomainLocationName.Create(input).Value);
        Assert.Equal(expected, DomainLocationName.From(input).Value);
    }

    [Fact]
    public void Should_TreatLocationNamesOfDifferentCasing_As_Equal()
    {
        Assert.Equal(DomainLocationName.Create("GPU"), DomainLocationName.From("gpu"));
        Assert.Equal(DomainLocationName.Create("GPU").GetHashCode(), DomainLocationName.From("gpu").GetHashCode());
    }
}
