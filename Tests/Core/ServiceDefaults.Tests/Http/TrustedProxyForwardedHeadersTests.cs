using System.Net;

using Microsoft.Extensions.Options;

namespace UserOrg.ServiceDefaults.Tests.Http;

public sealed class TrustedProxyForwardedHeadersTests
{
    private const string Production = "Production";
    private const string Development = "Development";

    private static readonly IPAddress Client = IPAddress.Parse("203.0.113.9");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Should_SetRemoteIpAddressToTheClient_When_ALoopbackPeerForwardsWithoutAConfiguredSection(string loopback)
    {
        await using var app = await TrustedProxyHost.StartAsync(Development, NoSection());

        var observed = await TrustedProxyHost.SendAsync(app, IPAddress.Parse(loopback), Client.ToString());

        Assert.Equal(Client, observed.RemoteIpAddress);
    }

    [Fact]
    public async Task Should_LeaveRemoteIpAddressUnchanged_When_AnUntrustedPeerForwards()
    {
        var untrusted = IPAddress.Parse("198.51.100.20");
        await using var app = await TrustedProxyHost.StartAsync(Development, NoSection());

        var observed = await TrustedProxyHost.SendAsync(app, untrusted, Client.ToString(), "https");

        Assert.Equal(untrusted, observed.RemoteIpAddress);
        Assert.Equal("http", observed.Scheme);
    }

    [Fact]
    public async Task Should_ApplyTheForwardedProto_When_ATrustedPeerForwards()
    {
        await using var app = await TrustedProxyHost.StartAsync(Development, NoSection());

        var observed = await TrustedProxyHost.SendAsync(app, IPAddress.Loopback, Client.ToString(), "https");

        Assert.Equal("https", observed.Scheme);
    }

    [Fact]
    public async Task Should_ApplyOnlyTheLastForwardedForEntry_When_ATrustedPeerForwardsAChain()
    {
        await using var app = await TrustedProxyHost.StartAsync(Development, NoSection());

        var observed = await TrustedProxyHost.SendAsync(app, IPAddress.Loopback, $"198.51.100.1, {Client}");

        Assert.Equal(Client, observed.RemoteIpAddress);
    }

    [Fact]
    public async Task Should_NoLongerTrustLoopback_When_KnownProxiesAreConfigured()
    {
        var proxy = IPAddress.Parse("10.0.0.5");
        await using var app = await TrustedProxyHost.StartAsync(Production, new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = proxy.ToString()
        });

        var fromLoopback = await TrustedProxyHost.SendAsync(app, IPAddress.Loopback, Client.ToString());
        var fromProxy = await TrustedProxyHost.SendAsync(app, proxy, Client.ToString());

        Assert.Equal(IPAddress.Loopback, fromLoopback.RemoteIpAddress);
        Assert.Equal(Client, fromProxy.RemoteIpAddress);
    }

    [Fact]
    public async Task Should_TrustAPeerInsideAConfiguredKnownNetwork()
    {
        await using var app = await TrustedProxyHost.StartAsync(Production, new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.20.0.0/16"
        });

        var inside = await TrustedProxyHost.SendAsync(app, IPAddress.Parse("10.20.3.4"), Client.ToString());
        var outside = await TrustedProxyHost.SendAsync(app, IPAddress.Parse("10.21.3.4"), Client.ToString());

        Assert.Equal(Client, inside.RemoteIpAddress);
        Assert.Equal(IPAddress.Parse("10.21.3.4"), outside.RemoteIpAddress);
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public async Task Should_FailOptionsValidation_When_KnownNetworksHoldAWildcard(string wildcard)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = wildcard
        };

        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TrustedProxyHost.StartAsync(Production, settings);
        });
    }

    [Theory]
    [InlineData("KnownProxies", "not-an-ip")]
    [InlineData("KnownProxies", "10.0.0.0/8")]
    [InlineData("KnownNetworks", "10.0.0.0/33")]
    [InlineData("KnownNetworks", "not-a-network/16")]
    public async Task Should_FailOptionsValidation_When_AnEntryDoesNotParse(string key, string entry)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"ForwardedHeaders:{key}:0"] = entry
        };

        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TrustedProxyHost.StartAsync(Production, settings);
        });
    }

    [Fact]
    public async Task Should_LogAWarning_When_AnIPv4KnownNetworkIsWiderThanSlash16()
    {
        var logs = new RecordingLoggerProvider();

        await using var app = await TrustedProxyHost.StartAsync(Production, new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8"
        }, logs);

        Assert.NotEmpty(logs.WarningsMentioning("10.0.0.0/8"));
    }

    [Fact]
    public async Task Should_NotLogAWideNetworkWarning_When_TheIPv4KnownNetworkIsSlash16()
    {
        var logs = new RecordingLoggerProvider();

        await using var app = await TrustedProxyHost.StartAsync(Production, new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.20.0.0/16"
        }, logs);

        var observed = await TrustedProxyHost.SendAsync(app, IPAddress.Parse("10.20.3.4"), Client.ToString());

        Assert.Equal(Client, observed.RemoteIpAddress);
        Assert.Empty(logs.WarningsMentioning("10.20.0.0/16"));
    }

    [Fact]
    public async Task Should_LogAWarningAtStart_When_TheSectionIsMissingOutsideDevelopment()
    {
        var logs = new RecordingLoggerProvider();

        await using var app = await TrustedProxyHost.StartAsync(Production, NoSection(), logs);

        Assert.NotEmpty(logs.WarningsMentioning("ForwardedHeaders"));
    }

    [Fact]
    public async Task Should_NotLogTheMissingSectionWarning_When_TheHostRunsInDevelopment()
    {
        var logs = new RecordingLoggerProvider();

        await using var app = await TrustedProxyHost.StartAsync(Development, NoSection(), logs);

        var observed = await TrustedProxyHost.SendAsync(app, IPAddress.Loopback, Client.ToString());

        Assert.Equal(Client, observed.RemoteIpAddress);
        Assert.Empty(logs.WarningsMentioning("ForwardedHeaders"));
    }

    [Fact]
    public async Task Should_NotLogTheMissingSectionWarning_When_TheSectionIsConfiguredOutsideDevelopment()
    {
        var logs = new RecordingLoggerProvider();

        await using var app = await TrustedProxyHost.StartAsync(Production, new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5"
        }, logs);

        Assert.Empty(logs.WarningsMentioning("ForwardedHeaders"));
    }

    private static Dictionary<string, string?> NoSection() => [];
}