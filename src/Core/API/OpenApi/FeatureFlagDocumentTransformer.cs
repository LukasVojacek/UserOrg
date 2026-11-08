using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// OpenAPI document transformer that removes endpoints disabled by feature flags.
/// </summary>
/// <remarks>
/// This transformer filters the OpenAPI specification to completely hide endpoints
/// when their associated feature flag is enabled. Disabled endpoints will not appear
/// in the generated OpenAPI schema, providing a clean API documentation that reflects
/// the current system state. The actual access is blocked at the middleware layer
/// (FeatureFlagMiddleware).
/// 
/// This transformer works in conjunction with FeatureFlagOperationTransformer and
/// FeatureFlagCleanupTransformer:
/// - OperationTransformer: marks disabled operations with [DISABLED] prefix
/// - DocumentTransformer: removes marked operations from paths
/// - CleanupTransformer: removes empty tags and paths (runs last)
/// 
/// This metadata-driven approach ensures each endpoint declares its own protection,
/// eliminating the need for hardcoded endpoint lists.
/// </remarks>
internal sealed class FeatureFlagDocumentTransformer : IOpenApiDocumentTransformer
{

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        if (document.Paths is null || document.Paths.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var (path, pathItem) in document.Paths)
        {
            if (pathItem.Operations is null || pathItem.Operations.Count == 0)
            {
                continue;
            }

            var operationsToRemove = new List<System.Net.Http.HttpMethod>();

            foreach (var (httpMethod, operation) in pathItem.Operations)
            {
                // Check if operation is marked as deprecated with [DISABLED] prefix by FeatureFlagOperationTransformer
                if (operation.Deprecated && operation.Description?.StartsWith("[DISABLED]") == true)
                {
                    operationsToRemove.Add(httpMethod);
                }
            }

            foreach (var httpMethod in operationsToRemove)
            {
                pathItem.Operations.Remove(httpMethod);
            }
        }

        return Task.CompletedTask;
    }
}