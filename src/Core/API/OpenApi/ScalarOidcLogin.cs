using Scalar.AspNetCore;

namespace UserOrganizationManagement.API.OpenApi;

internal static class ScalarOidcLogin
{
    public static WebApplicationBuilder AddScalarOidcLogin(this WebApplicationBuilder builder)
    {
        var options = builder.Services.AddOptions<ScalarOidcOptions>();

        if (builder.Environment.IsDevelopment())
        {
            options.BindConfiguration(ScalarOidcOptions.SectionName);
        }

        return builder;
    }

    public static ScalarOptions WithLoginSchemes(this ScalarOptions options, ScalarOidcOptions oidc)
    {
        if (!oidc.IsConfigured)
        {
            return options.AddPreferredSecuritySchemes([BearerSecuritySchemeTransformer.SchemeName]);
        }

        return options
            .AddPreferredSecuritySchemes([ScalarOidcOptions.SchemeName])
            .AddAuthorizationCodeFlow(ScalarOidcOptions.SchemeName, flow =>
            {
                flow.ClientId = oidc.ClientId;
                flow.Pkce = Pkce.Sha256;
                flow.CredentialsLocation = CredentialsLocation.Body;
                flow.SelectedScopes = oidc.Scopes;
            });
    }
}