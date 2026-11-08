namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Raised by <see cref="IResourceRequestProjectionStore.UpdateBySlugAsync"/> when the row was deleted between the
/// caller resolving the slug and this write landing — a delete-race, not the normal path. A dedicated type (rather
/// than the generic <see cref="InvalidOperationException"/> SQL Server itself throws for e.g. connection-pool
/// exhaustion) so callers can tell this permanent case apart from a transient one that happens to share the same
/// base exception type.
/// </summary>
public sealed class ResourceRequestRowMissingException(string slug)
    : Exception($"Resource request '{slug}' not found.")
{
    /// <summary>The slug that no longer resolves to a row.</summary>
    public string Slug { get; } = slug;
}