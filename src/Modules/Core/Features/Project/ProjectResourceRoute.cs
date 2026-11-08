using System.Diagnostics.CodeAnalysis;

using Core.Domain.Shared;
using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.Project;

/// <summary>
/// Route parsing, the read-only guard and cache eviction shared by the assign and unassign project-resource
/// endpoints, which address a resource by its per-provider natural key (ADR-0026/0031).
/// </summary>
internal static class ProjectResourceRoute
{
    internal sealed record RouteTarget(ProjectShortName ProjectShortName, ProjectResourceKey ResourceKey);

    internal static bool TryParse(
        string projectShortName,
        string providerName,
        string resourceName,
        [NotNullWhen(true)] out RouteTarget? target,
        [NotNullWhen(false)] out IResult? problem)
    {
        target = null;
        problem = null;

        if (!ProjectShortName.TryParse(projectShortName, null, out var parsedShortName))
            problem = Problems.Validation(ProjectErrorCodes.ShortNameInvalid, "Project short name is invalid.");
        else if (!ProviderName.TryCreate(providerName, out var parsedProviderName, out var providerNameError))
            problem = Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);
        else if (!ResourceName.TryParse(resourceName, null, out var parsedResourceName))
            problem = Problems.Validation(CommonErrorCodes.Required, "Resource name is required.");
        else
            target = new RouteTarget(parsedShortName, new ProjectResourceKey(parsedProviderName, parsedResourceName));

        return target is not null;
    }

    /// <summary>Cross-aggregate read-only guard, read off the loaded entity (ADR-0044 L2.5).</summary>
    internal static bool IsWritable(
        [NotNullWhen(true)] ResourceAggregate? resource,
        ResourceName resourceName,
        [NotNullWhen(false)] out IResult? problem)
    {
        problem = resource switch
        {
            null => Problems.NotFound("provider.resource_not_found", "Project resource not found."),
            { OperationMode: EOperationMode.ReadOnly } => Problems.Unprocessable(ProviderErrorCodes.ProviderReadOnly, $"Project resource '{resourceName.Value}' is in read-only mode and cannot be modified."),
            _ => null,
        };
        return problem is null;
    }

    /// <summary>
    /// Evicts the project tag, then the templates tag, which is keyed by the resource's surrogate (ADR-0016).
    /// The Guid is a storage detail confined to the cache-tag boundary (ADR-0044 L2.6).
    /// </summary>
    internal static async Task EvictAsync(
        IOutputCacheInvalidator cache, IResourceAssignmentQuery assignmentQuery, RouteTarget target, CancellationToken ct)
    {
        await cache.EvictAsync(CacheTags.Project(target.ProjectShortName.Value), ct);

        if (await assignmentQuery.GetResourceSurrogateAsync(target.ResourceKey, ct) is { } surrogate)
            await cache.EvictAsync(CacheTags.ProjectResource(surrogate), ct);
    }
}