namespace Forge.Core.Features.EndpointMap.Persistence;

internal class EndpointMapping
{
    internal long Id { get; init; }

    // logical name of the endpoint, e.g. "Project.Get" or "Admin.Stats"
    internal string EndpointName { get; set; } = string.Empty;

    internal string? Description { get; set; }

    // Path template used for path-based lookup. Example: "/projects/{projectShortName}/items/{id}" (required)
    internal string PathTemplate { get; set; } = string.Empty;

    // HTTP method for which this mapping applies (required). Stored as uppercase, e.g. "GET".
    internal string HttpMethod { get; set; } = string.Empty;

    internal ICollection<Permission.Persistence.Permission> Permissions { get; set; } = [];
}