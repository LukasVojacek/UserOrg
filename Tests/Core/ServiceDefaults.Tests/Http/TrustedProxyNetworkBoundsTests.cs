using Microsoft.Extensions.Options;

namespace UserOrg.ServiceDefaults.Tests.Http;

public sealed class TrustedProxyNetworkBoundsTests
{
    private const string Production = "Production";

    [Theory]
    [InlineData("::ffff:0:0/96")]
    [InlineData("::/64")]
    public async Task Should_FailOptionsValidation_When_AKnownNetworkCoversEveryIPv4MappedAddress(string network)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = network
        };

        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TrustedProxyHost.StartAsync(Production, settings);
        });
    }

    [Fact]
    public async Task Should_LogAWarning_When_AnIPv6KnownNetworkIsWiderThanSlash48()
    {
        using var logs = new RecordingLoggerProvider();

        await using var app = await TrustedProxyHost.StartAsync(Production, new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "2001:db8::/32"
        }, logs);

        Assert.NotEmpty(logs.WarningsMentioning("2001:db8::/32"));
    }

    [Fact]
    public async Task Should_NotLogAWideNetworkWarning_When_TheIPv6KnownNetworkIsSlash48()
    {
        using var logs = new RecordingLoggerProvider();

        await using var app = await TrustedProxyHost.StartAsync(Production, new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "2001:db8::/48"
        }, logs);

        Assert.Empty(logs.WarningsMentioning("2001:db8::/48"));
    }
}