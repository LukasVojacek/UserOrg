using Core.Shared.Paging;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Core.UnitTests.Shared;

public sealed class PageRequestTests
{
    [Theory]
    [InlineData(null, 100)] // missing -> default
    [InlineData(0, 100)]    // invalid -> default
    [InlineData(50, 50)]    // valid honored
    [InlineData(500, 300)]  // above max -> clamped
    public void Should_ResolveLimit_AgainstDefaultOptions(int? limit, int expected)
    {
        var page = new PageRequest(1, limit);

        Assert.Equal(expected, page.ResolveLimit(new PaginationOptions()));
    }

    [Fact]
    public void Should_HonorConfiguredBounds_When_OptionsOverridden()
    {
        var options = new PaginationOptions { DefaultLimit = 25, MaxLimit = 75 };

        Assert.Equal(25, new PageRequest(1, null).ResolveLimit(options));
        Assert.Equal(75, new PageRequest(1, 1000).ResolveLimit(options));
    }

    [Fact]
    public void Should_BindOptions_FromPaginationConfigSection()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Pagination:DefaultLimit"] = "120",
            ["Pagination:MaxLimit"] = "400",
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddOptions<PaginationOptions>().BindConfiguration(PaginationOptions.SectionName);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<PaginationOptions>>().Value;

        Assert.Equal(120, options.DefaultLimit);
        Assert.Equal(400, options.MaxLimit);
    }
}
