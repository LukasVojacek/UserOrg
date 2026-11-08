using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Startup;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes what <see cref="ServicesExtensions.AddAuthenticationServices"/> reads from configuration: the
/// role-management path prefix default, the Bearer audiences and the required IdP settings. Configuration is
/// in memory only; an empty string stands in for an absent value so the process environment cannot leak in.
/// </summary>
public sealed class ServicesExtensionsAuthenticationRegistrationTests
{
    private const string Audience = "userorg";

    [Theory]
    [InlineData("userorg/", "/userorg")]
    [InlineData("/userorg/", "/userorg")]
    [InlineData("", "")]
    public void Should_DefaultThePathPrefixToTheNormalizedGatewayPathBase(string pathBase, string expectedPrefix)
    {
        using var provider = BuildServices(new() { ["Gateway:PathBase"] = pathBase });

        Assert.Equal(expectedPrefix, provider.GetRequiredService<IOptions<RoleManagementOptions>>().Value.PathPrefix);
    }

    [Fact]
    public void Should_PreferGatewayDoubleUnderscorePathBase_Over_GatewayColonPathBase()
    {
        using var provider = BuildServices(new()
        {
            ["GATEWAY__PATHBASE"] = "/from-env-key",
            ["Gateway:PathBase"] = "/from-section",
        });

        Assert.Equal("/from-env-key", provider.GetRequiredService<IOptions<RoleManagementOptions>>().Value.PathPrefix);
    }

    [Fact]
    public void Should_KeepAnExplicitPathPrefix_When_GatewayPathBaseIsAlsoSet()
    {
        using var provider = BuildServices(
            new() { ["Gateway:PathBase"] = "/from-section" },
            services => services.Configure<RoleManagementOptions>(options => options.PathPrefix = "/explicit"));

        Assert.Equal("/explicit", provider.GetRequiredService<IOptions<RoleManagementOptions>>().Value.PathPrefix);
    }

    [Fact]
    public void Should_AddTheTrimmedDistinctAdditionalAudiences_AfterTheAudience()
    {
        using var provider = BuildServices(new() { ["Idp:AdditionalAudiences"] = " b ,a,,b,A" });

        Assert.Equal(new[] { Audience, "b", "a", "A" }, BearerAudiences(provider));
    }

    [Fact]
    public void Should_ValidateOnlyTheAudience_When_AdditionalAudiencesIsEmpty()
    {
        using var provider = BuildServices(new() { ["Idp:AdditionalAudiences"] = "" });

        Assert.Equal(new[] { Audience }, BearerAudiences(provider));
    }

    [Fact]
    public void Should_ThrowForAuthAuthority_When_IdpAuthorityIsEmpty()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            using var provider = BuildServices(new() { ["Idp:Authority"] = "" });
        });

        Assert.Equal("authAuthority", exception.ParamName);
    }

    [Fact]
    public void Should_ThrowForAudience_When_IdpAudienceIsEmpty()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
        {
            using var provider = BuildServices(new() { ["Idp:Audience"] = "" });
        });

        Assert.Equal("audience", exception.ParamName);
    }

    private static IEnumerable<string> BearerAudiences(ServiceProvider provider)
        => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(Constants.BearerSchemeId)
            .TokenValidationParameters
            .ValidAudiences;

    /// <summary>
    /// The gateway's authentication registration in Production with valid IdP settings, overridden by
    /// <paramref name="overrides"/>; <paramref name="configureServices"/> runs before the registration.
    /// </summary>
    private static ServiceProvider BuildServices(
        Dictionary<string, string?> overrides,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        var settings = new Dictionary<string, string?>
        {
            ["Idp:Authority"] = "https://idp.example.test",
            ["Idp:Audience"] = Audience,
            ["Idp:AdditionalAudiences"] = "",
            ["Gateway:PathBase"] = "",
            ["FeatureManagement:StrictJwtAudience"] = "",
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        configureServices?.Invoke(builder.Services);
        builder.AddAuthenticationServices();

        return builder.Services.BuildServiceProvider();
    }
}
