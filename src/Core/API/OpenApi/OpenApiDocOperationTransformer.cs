using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

using UserOrg.Shared.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// Applies the documentation delegates registered by <c>WithOpenApiDoc(...)</c> to the generated operation.
/// </summary>
/// <remarks>
/// ASP.NET Core's own <c>WithOpenApi(...)</c> convention stores its delegate in a metadata slot that the
/// .NET 10 <c>AddOpenApi</c> document pipeline does not consult, so summaries, descriptions and parameter
/// descriptions written through it never reached the document. This transformer restores that behaviour by
/// reading <see cref="OpenApiDocMetadata"/> off the endpoint and invoking the delegate against the live
/// operation.
///
/// Ordering: this transformer MUST be registered before <see cref="FeatureFlagOperationTransformer"/>.
/// That transformer prefixes <c>"[DISABLED] "</c> onto <see cref="OpenApiOperation.Description"/>, and
/// <see cref="FeatureFlagDocumentTransformer"/> later removes operations by matching that exact prefix. A
/// documentation delegate assigns <c>Description</c> outright, so running it afterwards would erase the
/// marker and leave a flag-disabled endpoint published in the spec.
/// </remarks>
internal sealed class OpenApiDocOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata is null)
        {
            return Task.CompletedTask;
        }

        // Endpoint metadata is ordered; more than one delegate is legal, and later ones win, which matches
        // how repeated conventions behave elsewhere.
        foreach (var doc in metadata.OfType<OpenApiDocMetadata>())
        {
            try
            {
                doc.Configure(operation);
            }
#pragma warning disable CA1031 // Rethrown below; the catch exists only to name the endpoint.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                // These delegates were inert for as long as WithOpenApi was, so some index their Parameters
                // list by a position the endpoint no longer has. That surfaces during document generation,
                // inside an MSBuild target, where the bare stack trace names no endpoint. Rethrowing is the
                // point - such a delegate is wrong and has to be fixed - but it should say which one.
                throw new InvalidOperationException(
                    $"The OpenAPI documentation delegate for '{context.Description.HttpMethod} {context.Description.RelativePath}' failed. The operation has {operation.Parameters?.Count ?? 0} parameter(s); a delegate indexing past that is the usual cause.",
                    exception);
            }
        }

        return Task.CompletedTask;
    }
}