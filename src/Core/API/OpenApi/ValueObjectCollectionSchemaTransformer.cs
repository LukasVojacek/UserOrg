using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// Restores the missing <c>items</c> schema on collections of value objects such as
/// <see cref="ProjectShortName"/> and <see cref="ProjectId"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both value objects carry a <c>[JsonConverter]</c>. System.Text.Json's <c>JsonSchemaExporter</c> — which
/// Microsoft.AspNetCore.OpenApi uses to seed every schema — cannot describe a type behind a custom converter,
/// so it emits the unconstrained schema <c>true</c> for it. For a collection it then emits
/// <c>{"type":["array","null"]}</c> with <b>no <c>items</c> key at all</b>, because there is no element schema
/// worth writing.
/// </para>
/// <para>
/// The consequence is that <c>OpenApiSchema.Items</c> is <see langword="null"/>, and since the schema
/// transformer pipeline only walks sub-schemas that exist, the <c>ProjectShortName</c>/<c>ProjectId</c> type
/// mappings registered in <c>ConfigureSharedOpenApi</c> are never given an element schema to run on. The array
/// therefore reaches the document as a bare <c>type: array</c>, which tells a client generator nothing about the
/// element type (Spectral <c>array-items</c>).
/// </para>
/// <para>
/// This transformer creates that missing element schema. Once <c>Items</c> exists the pipeline recurses into it
/// and the existing type mappings apply to it as they would to any other schema.
/// </para>
/// </remarks>
internal sealed class ValueObjectCollectionSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        if (schema.Items is not null || schema.Type is not { } type || !type.HasFlag(JsonSchemaType.Array))
        {
            return Task.CompletedTask;
        }

        var elementType = context.JsonTypeInfo.ElementType;

        if (elementType == typeof(ProjectShortName))
        {
            schema.Items = new OpenApiSchema { Type = JsonSchemaType.String };
        }
        else if (elementType == typeof(ProjectId))
        {
            schema.Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" };
        }

        return Task.CompletedTask;
    }
}