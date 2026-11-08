using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// ADR-0045 gate 1 (load-bearing safety): the Develop-only override scheme must exist ONLY in
/// Development. These tests run the real <see cref="ServicesExtensions.AddAuthenticationServices"/>
/// wiring and assert the scheme's presence/absence by environment.
/// </summary>
public sealed class DevAuthOverrideRegistrationTests
{
    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    public async Task DevOverrideScheme_IsRegistered_OnlyInDevelopment(string environment, bool expectedRegistered)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // AddAuthenticationServices requires a configured IdP authority/audience.
            ["Idp:Authority"] = "https://idp.example.test",
            ["Idp:Audience"] = "userorg"
        });

        builder.AddAuthenticationServices();

        await using var provider = builder.Services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = await schemeProvider.GetAllSchemesAsync();

        var hasOverride = schemes.Any(s => s.Name == UserOrg.Gateway.Constants.DevOverrideSchemeId);
        Assert.Equal(expectedRegistered, hasOverride);
    }
}
