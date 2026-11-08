using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

internal sealed class OidcSecuritySchemeTransformer(IOptions<ScalarOidcOptions> options) : IOpenApiDocumentTransformer
{
    private readonly ScalarOidcOptions _oidc = options.Value;

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (!_oidc.IsConfigured)
        {
            return Task.CompletedTask;
        }

        RegisterScheme(document, CreateScheme(_oidc));

        var requirement = new OpenApiSecurityRequirement
        {
            { new OpenApiSecuritySchemeReference(ScalarOidcOptions.SchemeName, document), [.. _oidc.Scopes] }
        };

        foreach (var operation in OperationsOf(document))
        {
            operation.Security ??= [];
            operation.Security.Add(requirement);
        }

        return Task.CompletedTask;
    }

    private static void RegisterScheme(OpenApiDocument document, OpenApiSecurityScheme scheme)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ScalarOidcOptions.SchemeName] = scheme;
    }

    private static OpenApiSecurityScheme CreateScheme(ScalarOidcOptions oidc) => new()
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = oidc.AuthorizationUrl,
                TokenUrl = oidc.TokenUrl,
                Scopes = oidc.Scopes.ToDictionary(static scope => scope, static scope => scope)
            }
        }
    };

    private static IEnumerable<OpenApiOperation> OperationsOf(OpenApiDocument document) =>
        document.Paths.Values.SelectMany(static path => path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>());
}