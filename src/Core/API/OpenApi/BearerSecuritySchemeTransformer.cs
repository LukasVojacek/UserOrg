using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    internal const string SchemeName = "Bearer";

    private static readonly OpenApiSecurityScheme _bearerScheme = new()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste only the JWT access token. The Bearer prefix is applied automatically."
    };

    private static readonly OpenApiSecurityRequirement _bearerRequirement = new()
    {
        { new OpenApiSecuritySchemeReference(SchemeName), [] }
    };

    private static readonly OpenApiSecurityScheme _serviceScheme = new()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Paste only the service token in format: Service TOKEN/KEY"
    };

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = _bearerScheme;
        document.Components.SecuritySchemes["Service"] = _serviceScheme;

        if (document.Paths is null)
            return Task.CompletedTask;

        foreach (var pathItem in document.Paths.Values)
        {
            if (pathItem.Operations is null)
                continue;

            foreach (var operation in pathItem.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(_bearerRequirement);
            }
        }

        return Task.CompletedTask;
    }
}