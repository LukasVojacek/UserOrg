namespace Forge.Core.Features.EndpointMap.Services;

public sealed record EndpointPermissionMatch(string EndpointName, IReadOnlyList<string> PermissionTemplates, IReadOnlyDictionary<string, string> RouteValues)
{
    /// <summary>
    /// The mapping's path template, with its placeholders left standing - <c>/api/Project/{projectShortName}</c>,
    /// never <c>/api/Project/TESTPRJ</c>. Bounded by the number of seeded mappings, which is what makes it
    /// safe to use where a concrete path would not be: a trace span name, a metric dimension.
    /// </summary>
    public string? PathTemplate { get; init; }
}