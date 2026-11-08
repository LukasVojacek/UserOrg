using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

internal sealed class PascalCasingSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Properties is { Count: > 0 })
        {
            schema.Properties = schema.Properties.ToDictionary(
                kvp => char.ToUpper(kvp.Key[0], System.Globalization.CultureInfo.CurrentCulture) + kvp.Key[1..],
                kvp => kvp.Value);
        }

        return Task.CompletedTask;
    }
}