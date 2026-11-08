using Heappe.Features.Connection;

using Xunit;

using static Heappe.UnitTests.Features.Connection.TokenExchangeValues;

namespace Heappe.UnitTests.Features.Connection;

public sealed class TokenExchangeOptionsValidatorTests
{
    private static TokenExchangeOptions Valid() => new()
    {
        ClientId = IntrospectionClientId,
        ClientSecret = IntrospectionSecret,
        Authority = new Uri(Authority),
    };

    [Fact]
    public void A_valid_configuration_succeeds()
    {
        var result = new TokenExchangeOptionsValidator(true).Validate(null, Valid());

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Theory]
    [InlineData(null, IntrospectionSecret, Authority)]
    [InlineData("", IntrospectionSecret, Authority)]
    [InlineData("  ", IntrospectionSecret, Authority)]
    [InlineData(IntrospectionClientId, null, Authority)]
    [InlineData(IntrospectionClientId, "", Authority)]
    [InlineData(IntrospectionClientId, IntrospectionSecret, null)]
    public void A_missing_client_configuration_fails_with_the_flag_on(string? clientId, string? clientSecret, string? authority)
    {
        var options = new TokenExchangeOptions
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            Authority = authority is null ? null : new Uri(authority),
        };

        var result = new TokenExchangeOptionsValidator(true).Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void A_missing_client_configuration_succeeds_with_the_flag_off()
    {
        var result = new TokenExchangeOptionsValidator(false).Validate(null, new TokenExchangeOptions());

        Assert.True(result.Succeeded);
    }
}
