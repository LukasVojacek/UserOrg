using UserOrg.Gateway.Features.InternalToken;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes <see cref="InternalTokenOptions.Validate"/>: the defaults are valid, and each rule rejects
/// its value with an <see cref="ArgumentException"/> naming the offending option.
/// </summary>
public sealed class InternalTokenOptionsTests
{
    [Fact]
    public void Should_Pass_When_TheDefaultsAreUsed()
    {
        var exception = Record.Exception(() => new InternalTokenOptions().Validate());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(1, 1, 0, 1, 59)]
    [InlineData(2, 1, 0, 1, 1)]
    [InlineData(60, 24, 48, 30, 3599)]
    public void Should_Pass_When_EveryValueIsOnItsBoundary(int expirationMinutes, int rotationHours, int graceHours, int jwksSeconds, int tokenCacheSeconds)
    {
        var options = new InternalTokenOptions
        {
            ExpirationMinutes = expirationMinutes,
            KeyRotationIntervalHours = rotationHours,
            KeyGracePeriodHours = graceHours,
            JwksCacheExpirationSeconds = jwksSeconds,
            TokenCacheSeconds = tokenCacheSeconds,
        };

        var exception = Record.Exception(options.Validate);

        Assert.Null(exception);
    }

    public static TheoryData<string, string, string> InvalidOptions() => new()
    {
        { "Issuer", "", "The value cannot be an empty string or composed entirely of whitespace." },
        { "Issuer", " ", "The value cannot be an empty string or composed entirely of whitespace." },
        { "Audience", "", "The value cannot be an empty string or composed entirely of whitespace." },
        { "ExpirationMinutes", "0", "ExpirationMinutes must be greater than 0" },
        { "KeyRotationIntervalHours", "0", "KeyRotationIntervalHours must be greater than 0" },
        { "KeyGracePeriodHours", "-1", "KeyGracePeriodHours cannot be negative" },
        { "JwksCacheExpirationSeconds", "0", "JwksCacheExpirationSeconds must be greater than 0" },
        { "KeyAlgorithm", "99", "Invalid KeyAlgorithm: 99" },
        { "TokenCacheSeconds", "0", "TokenCacheSeconds must be between 1 and 3599" },
        { "TokenCacheSeconds", "3600", "TokenCacheSeconds must be between 1 and 3599" },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Should_ThrowNamingTheOption_When_AValueIsInvalid(string option, string value, string message)
    {
        var options = new InternalTokenOptions();
        Set(options, option, value);

        var exception = Assert.ThrowsAny<ArgumentException>(options.Validate);

        Assert.Equal(option, exception.ParamName);
        Assert.Equal($"{message} (Parameter '{option}')", exception.Message);
    }

    [Fact]
    public void Should_ThrowForTheNullIssuer_When_TheIssuerIsNull()
    {
        var options = new InternalTokenOptions { Issuer = null! };

        var exception = Assert.Throws<ArgumentNullException>(options.Validate);

        Assert.Equal(nameof(InternalTokenOptions.Issuer), exception.ParamName);
    }

    private static void Set(InternalTokenOptions options, string option, string value)
    {
        switch (option)
        {
            case nameof(InternalTokenOptions.Issuer):
                options.Issuer = value;
                break;
            case nameof(InternalTokenOptions.Audience):
                options.Audience = value;
                break;
            case nameof(InternalTokenOptions.KeyAlgorithm):
                options.KeyAlgorithm = (KeyAlgorithm)int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
            default:
                typeof(InternalTokenOptions).GetProperty(option)!.SetValue(options, int.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }
}
