using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// Filters OpenAPI document paths based on document name.
/// User document includes all non-admin paths; Administration document includes only admin paths.
/// Replaces Swashbuckle DocInclusionPredicate without touching individual endpoint files.
/// </summary>
internal sealed class DocumentPathGroupFilter : IOpenApiDocumentTransformer
{
    private static readonly IReadOnlyList<string> AdminPathSegments =
        ["Administration", "Location", "Provider", "api/Role", "KillSwitch"];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (document.Paths is null)
            return Task.CompletedTask;

        var isAdminDoc = context.DocumentName.Equals("Administration", StringComparison.OrdinalIgnoreCase);

        var pathsToRemove = document.Paths
            .Where(kvp =>
            {
                var isAdminPath = AdminPathSegments.Any(segment =>
                    kvp.Key.Contains(segment, StringComparison.OrdinalIgnoreCase));
                return isAdminDoc ? !isAdminPath : isAdminPath;
            })
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var path in pathsToRemove)
        {
            document.Paths.Remove(path);
        }

        return Task.CompletedTask;
    }
}