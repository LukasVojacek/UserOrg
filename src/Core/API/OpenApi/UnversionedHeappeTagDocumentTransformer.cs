using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// Describes every document tag carried by an <c>/api/Heappe/</c> operation as unversioned: those routes have
/// no <c>/api/v2</c> counterpart and keep their path after v1 is retired. Registered on the Service document only.
/// </summary>
internal sealed class UnversionedHeappeTagDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string HeappePathPrefix = "/api/Heappe/";

    private const string Note = "Served unversioned at /api/Heappe by the HEAppE module. These routes are not part of the v1 surface: they have no /api/v2 counterpart and keep this path after v1 is retired.";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Paths is null || document.Tags is null)
            return Task.CompletedTask;

        var heappeTags = document.Paths
            .Where(path => path.Key.StartsWith(HeappePathPrefix, StringComparison.Ordinal))
            .SelectMany(path => path.Value.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            .SelectMany(operation => operation.Tags ?? Enumerable.Empty<OpenApiTagReference>())
            .Select(tag => tag.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var tag in document.Tags.Where(tag => tag.Name is not null && heappeTags.Contains(tag.Name)))
        {
            tag.Description = Note;
        }

        return Task.CompletedTask;
    }
}