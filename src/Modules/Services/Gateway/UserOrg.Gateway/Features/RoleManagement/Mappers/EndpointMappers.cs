using Forge.Core.Features.EndpointMap.Services;

namespace UserOrg.Gateway.Features.RoleManagement.Mappers;

/// <summary>
/// Extension methods for mapping between Forge.Core EndpointPermissionMap and Gateway DTOs.
/// </summary>
internal static class EndpointMappers
{
    /// <summary>
    /// Converts Forge.Core EndpointPermissionMap to Gateway EndpointMappingInfo DTO.
    /// </summary>
    /// <param name="endpointMap">The source endpoint permission map.</param>
    /// <returns>Endpoint mapping information DTO.</returns>
    internal static EndpointMappingInfo ToEndpointMappingInfo(this EndpointPermissionMap endpointMap) =>
        new()
        {
            EndpointName = endpointMap.EndpointName,
            Permissions = [.. endpointMap.Permissions],
            PathTemplate = endpointMap.PathTemplate,
            HttpMethod = endpointMap.HttpMethod,
            Description = endpointMap.Description
        };

    /// <summary>
    /// Converts endpoint name and permissions to EndpointPermissionMap for Forge.Core.
    /// </summary>
    /// <param name="endpointName">Name of the endpoint.</param>
    /// <param name="permissions">Collection of permission names.</param>
    /// <returns>Endpoint permission map for Forge.Core service calls.</returns>
    internal static EndpointPermissionMap ToEndpointPermissionMap(string endpointName, IEnumerable<string> permissions) =>
        new(endpointName, [.. permissions]);
}