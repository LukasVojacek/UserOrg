using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// How <c>FeatureManagement:StrictJwtAudience</c> shapes the Bearer scheme's audience check (ADR 0121): a token is
/// validated against the registered parameters with a test signing key, so only the audience decides.
/// </summary>
public sealed class StrictJwtAudienceRegistrationTests
{
    private const string Authority = "https://idp.example.test";
    private const string Audience = "userorg";
    private const string FlagKey = "FeatureManagement:StrictJwtAudience";

    private static readonly SymmetricSecurityKey SigningKey = new("strict-audience-registration-signing-key-0123456789"u8.ToArray());

    [Fact]
    public async Task Should_RejectWithInvalidAudience_When_StrictAndTheTokenCarriesAnExtraAudience()
    {
        using var provider = BuildServices(new() { [FlagKey] = "" });

        var result = await ValidateAsync(provider, [Audience, "other"]);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    [Fact]
    public async Task Should_Accept_When_StrictAndEveryAudienceIsConfigured()
    {
        using var provider = BuildServices(new() { [FlagKey] = "true", ["Idp:AdditionalAudiences"] = "b" });

        var result = await ValidateAsync(provider, [Audience, "b"]);

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public async Task Should_Accept_When_OffAndOneAudienceMatches()
    {
        using var provider = BuildServices(new() { [FlagKey] = "false" });

        var result = await ValidateAsync(provider, [Audience, "other"]);

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("true")]
    public void Should_SetTheAudienceValidator_When_TheFlagIsOnOrBlank(string flag)
    {
        using var provider = BuildServices(new() { [FlagKey] = flag });

        Assert.NotNull(BearerParameters(provider).AudienceValidator);
    }

    [Fact]
    public void Should_LeaveTheDefaultAudienceCheck_When_TheFlagIsFalse()
    {
        using var provider = BuildServices(new() { [FlagKey] = "false" });

        Assert.Null(BearerParameters(provider).AudienceValidator);
    }

    [Fact]
    public void Should_StopStartup_When_TheFlagIsNotABoolean()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            using var provider = BuildServices(new() { [FlagKey] = "maybe" });
        });

        Assert.Contains(FlagKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("maybe", exception.Message, StringComparison.Ordinal);
    }

    private static TokenValidationParameters BearerParameters(ServiceProvider provider)
        => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(Constants.BearerSchemeId)
            .TokenValidationParameters;

    private static async Task<TokenValidationResult> ValidateAsync(ServiceProvider provider, string[] audiences)
    {
        var parameters = BearerParameters(provider).Clone();
        parameters.IssuerSigningKey = SigningKey;

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = "strict-audience-user",
                [JwtRegisteredClaimNames.Aud] = audiences,
            },
        });

        return await handler.ValidateTokenAsync(token, parameters);
    }

    /// <summary>
    /// The gateway's authentication registration in Production with valid IdP settings and the flag blank,
    /// overridden by <paramref name="overrides"/>.
    /// </summary>
    private static ServiceProvider BuildServices(Dictionary<string, string?> overrides)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        var settings = new Dictionary<string, string?>
        {
            ["Idp:Authority"] = Authority,
            ["Idp:Audience"] = Audience,
            ["Idp:AdditionalAudiences"] = "",
            ["Gateway:PathBase"] = "",
            [FlagKey] = "",
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddAuthenticationServices();

        return builder.Services.BuildServiceProvider();
    }
}
