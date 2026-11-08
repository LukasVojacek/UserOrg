using System.Text.Json.Nodes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// Normalises nullable enum schemas. The framework emits string enums whose nullability appears as a
/// bare <c>null</c> entry in the <c>enum</c> list with no declared <c>type</c> (e.g. ERequestState,
/// ERequestType). Spectral's <c>oas</c> ruleset crashes on that shape ("Cannot read properties of
/// null (reading 'enum')"), so we strip the null member, mark the schema nullable and ensure a string
/// type — yielding a clean <c>type: string, nullable: true</c> enum the linter can process.
/// </summary>
internal sealed class NullableEnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Enum is { Count: > 0 } && schema.Enum.Any(static e => e is null))
        {
            schema.Enum = schema.Enum.Where(static e => e is not null).ToList<JsonNode>();
            schema.Type = (schema.Type ?? JsonSchemaType.String) | JsonSchemaType.Null;
        }

        return Task.CompletedTask;
    }
}