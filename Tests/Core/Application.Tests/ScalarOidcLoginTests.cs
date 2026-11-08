using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Scalar.AspNetCore;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

public sealed class ScalarOidcLoginTests
{
    private static readonly Dictionary<string, string?> OidcSettings = new()
    {
        ["Scalar:Oidc:AuthorizationUrl"] = "http://localhost:8080/realms/master/protocol/openid-connect/auth",
        ["Scalar:Oidc:TokenUrl"] = "http://localhost:8080/realms/master/protocol/openid-connect/token",
        ["Scalar:Oidc:ClientId"] = "scalar",
    };

    [Fact(DisplayName = "Should_Not_Bind_Oidc_Options_Outside_Development")]
    public async Task ShouldNotBindOidcOptionsOutsideDevelopment()
    {
        var oidc = await BindOidcOptionsAsync(Environments.Production);

        Assert.False(oidc.IsConfigured);
    }

    [Fact(DisplayName = "Should_Bind_Oidc_Options_In_Development")]
    public async Task ShouldBindOidcOptionsInDevelopment()
    {
        var oidc = await BindOidcOptionsAsync(Environments.Development);

        Assert.True(oidc.IsConfigured);
        Assert.Equal("scalar", oidc.ClientId);
    }

    [Fact(DisplayName = "Should_Prefer_Bearer_And_Register_No_Flow_When_Not_Configured")]
    public void ShouldPreferBearerAndRegisterNoFlowWhenNotConfigured()
    {
        var options = new ScalarOptions().WithLoginSchemes(new ScalarOidcOptions());

        Assert.Equal(["Bearer"], options.Authentication?.PreferredSecuritySchemes ?? []);
        Assert.False(options.Authentication?.SecuritySchemes?.ContainsKey("Oidc") ?? false);
    }

    [Fact(DisplayName = "Should_Prefer_Oidc_When_Configured")]
    public void ShouldPreferOidcWhenConfigured()
    {
        var options = new ScalarOptions().WithLoginSchemes(ConfiguredOptions());

        Assert.Equal(["Oidc"], options.Authentication?.PreferredSecuritySchemes ?? []);
    }

    [Fact(DisplayName = "Should_Register_A_Public_Pkce_Code_Flow_Without_A_Client_Secret_In_Scalar")]
    public void ShouldRegisterAPublicPkceCodeFlowWithoutAClientSecretInScalar()
    {
        var options = new ScalarOptions().WithLoginSchemes(ConfiguredOptions());

        var scheme = Assert.IsType<ScalarOAuth2SecurityScheme>(options.Authentication?.SecuritySchemes?["Oidc"]);
        var flow = scheme.Flows?.AuthorizationCode;
        Assert.NotNull(flow);
        Assert.Equal("scalar", flow.ClientId);
        Assert.Null(flow.ClientSecret);
        Assert.Equal(Pkce.Sha256, flow.Pkce);
        Assert.Equal(CredentialsLocation.Body, flow.CredentialsLocation);
        Assert.Equal(["openid", "profile"], flow.SelectedScopes ?? []);
    }

    private static ScalarOidcOptions ConfiguredOptions() => new()
    {
        AuthorizationUrl = new Uri(OidcSettings["Scalar:Oidc:AuthorizationUrl"]!),
        TokenUrl = new Uri(OidcSettings["Scalar:Oidc:TokenUrl"]!),
        ClientId = "scalar",
    };

    private static async Task<ScalarOidcOptions> BindOidcOptionsAsync(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environmentName });
        builder.Configuration.AddInMemoryCollection(OidcSettings);

        builder.AddScalarOidcLogin();

        await using var app = builder.Build();
        return app.Services.GetRequiredService<IOptions<ScalarOidcOptions>>().Value;
    }
}
