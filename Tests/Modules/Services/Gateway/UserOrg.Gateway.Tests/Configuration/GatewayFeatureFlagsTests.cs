using Microsoft.Extensions.Configuration;

using UserOrg.Gateway.Configuration;
using UserOrg.Shared.FeatureManagement;

namespace UserOrg.Gateway.Tests.Configuration;

/// <summary>
/// A gateway flag that defaults on: absent or blank means on, a boolean is taken as written, anything else
/// stops the gateway with a message that names the key and never echoes the value.
/// </summary>
public sealed class GatewayFeatureFlagsTests
{
    private const string Key = "FeatureManagement:StrictJwtAudience";

    [Fact]
    public void Should_BeOn_When_TheKeyIsAbsent() =>
        Assert.True(GatewayFeatureFlags.IsEnabledByDefault(new ConfigurationBuilder().Build(), FeatureFlags.StrictJwtAudience));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_BeOn_When_TheValueIsBlank(string value) =>
        Assert.True(GatewayFeatureFlags.ParseDefaultOn(value, Key));

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData(" true ")]
    public void Should_BeOn_When_TheValueIsTrue(string value) =>
        Assert.True(GatewayFeatureFlags.ParseDefaultOn(value, Key));

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    public void Should_BeOff_When_TheValueIsFalse(string value) =>
        Assert.False(GatewayFeatureFlags.ParseDefaultOn(value, Key));

    [Fact]
    public void Should_ThrowNamingTheKeyOnly_When_TheValueIsNotABoolean()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GatewayFeatureFlags.ParseDefaultOn("on-please", Key));

        Assert.Contains(Key, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("on-please", exception.Message, StringComparison.Ordinal);
    }
}
