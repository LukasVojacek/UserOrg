using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Forge.Core.Features.EndpointMap.Services;

public interface IEndpointPermissionService
{
    Task<Result<IList<string>, Error>> GetEndpointPermissionsAsync(string endpointName, CancellationToken cancellationToken);
    Task<IList<EndpointPermissionMap>> GetEndpointMappingsAsync(string endpointName, CancellationToken cancellationToken);
    Task<Result<Error>> SetEndpointPermissionAsync(EndpointPermissionMap endpointPermission, CancellationToken cancellationToken);
    // Lookup permissions for a request path + HTTP method. Returns matched permission templates (may contain placeholders)
    // Implementation should use an in-memory snapshot of endpoint mappings and return route captures for materialization.
    Task<Result<EndpointPermissionMatch?, Error>> GetEndpointPermissionsByPathAsync(string path, string httpMethod, CancellationToken cancellationToken);

    Task<Result<EndpointPermissionMap, Error>> CreateEndpointMappingAsync(string endpointName, string pathTemplate, string httpMethod, string? description, CancellationToken cancellationToken);
    Task<Result<Error>> DeleteEndpointMappingAsync(string endpointName, CancellationToken cancellationToken);
    Task<Result<EndpointPermissionMap, Error>> GetEndpointMappingByNameAsync(string endpointName, CancellationToken cancellationToken);

    /// <summary>
    /// Evicts the cached endpoint-permission snapshot so the next lookup rebuilds it from the
    /// database. Callers outside this service (e.g. importers) use this instead of learning the
    /// underlying cache key.
    /// </summary>
    /// <param name="cancellationToken">Token for operation cancellation.</param>
    Task InvalidateSnapshotAsync(CancellationToken cancellationToken);
}