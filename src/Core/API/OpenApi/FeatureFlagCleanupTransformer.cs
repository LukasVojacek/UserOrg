using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// OpenAPI document transformer that removes empty tags and paths after operations are filtered.
/// </summary>
/// <remarks>
/// This transformer runs last in the transformation pipeline and cleans up the OpenAPI document
/// by removing:
/// - Empty tags (tags with no associated operations)
/// - Empty paths (paths with no operations)
///
/// This is useful when operations are removed by other transformers (e.g., FeatureFlagDocumentTransformer)
/// and leave behind empty containers. Running this cleanup transformer ensures a clean API
/// specification without orphaned tags or paths, particularly important when switching between
/// different API specifications (e.g., User vs Administration) or when feature flags hide entire
/// operation groups.
///
/// This transformer should be registered LAST in the OpenAPI transformation pipeline to ensure
/// all other removals have been completed before cleanup occurs.
/// </remarks>
internal sealed class FeatureFlagCleanupTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);

        var usedTags = new HashSet<string>(StringComparer.Ordinal);

        if (document.Paths is not null)
        {
            foreach (var pathItem in document.Paths.Values)
            {
                if (pathItem.Operations is null)
                {
                    continue;
                }

                foreach (var operation in pathItem.Operations.Values)
                {
                    if (operation.Tags is not null)
                    {
                        foreach (var tag in operation.Tags)
                        {
                            // OpenApiTag can have null Name, so check before adding
                            if (!string.IsNullOrWhiteSpace(tag.Name))
                            {
                                usedTags.Add(tag.Name);
                            }
                        }
                    }
                }
            }
        }

        if (document.Tags is not null && document.Tags.Count > 0)
        {
            var tagsToRemove = document.Tags
                .Where(tag => string.IsNullOrWhiteSpace(tag.Name) || !usedTags.Contains(tag.Name))
                .ToList();

            foreach (var tag in tagsToRemove)
            {
                document.Tags.Remove(tag);
            }
        }

        if (document.Paths is not null && document.Paths.Count > 0)
        {
            var pathsToRemove = document.Paths
                .Where(kvp => kvp.Value.Operations is null || kvp.Value.Operations.Count == 0)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var path in pathsToRemove)
            {
                document.Paths.Remove(path);
            }
        }

        return Task.CompletedTask;
    }
}