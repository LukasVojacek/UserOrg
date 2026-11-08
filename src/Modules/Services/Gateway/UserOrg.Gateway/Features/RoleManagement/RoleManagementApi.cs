using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Features.RoleManagement;

internal sealed record RbacEntry(string Endpoint, string[] Roles, string[] Permissions, string? HttpMethod);

internal static class RoleManagementApi
{
    internal static async Task<IEnumerable<RbacEntry>> BuildRbacAsync(IRoleManagementService service, CancellationToken cancellationToken)
    {
        var endpointsResult = await service.GetEndpointMappingsAsync(cancellationToken);
        if (!endpointsResult.IsSuccess)
        {
            throw new InvalidOperationException("Failed to retrieve endpoint mappings");
        }

        var rolesResult = await service.GetRolesAsync(cancellationToken);
        if (!rolesResult.IsSuccess)
        {
            throw new InvalidOperationException("Failed to retrieve roles");
        }

        var roleDetails = new Dictionary<string, RoleDetail>(StringComparer.OrdinalIgnoreCase);
        foreach (var rs in rolesResult.Value)
        {
            var detailRes = await service.GetRoleAsync(rs.Name, cancellationToken);
            if (detailRes.IsSuccess)
            {
                roleDetails[rs.Name] = detailRes.Value;
            }
        }

        var list = new List<RbacEntry>();
        foreach (var ep in endpointsResult.Value)
        {
            var endpointKey = string.IsNullOrWhiteSpace(ep.PathTemplate) ? ep.EndpointName : ep.PathTemplate!;
            var epPerms = ep.Permissions ?? Array.Empty<string>();

            var allowedRoles = roleDetails
                .Where(kv => kv.Value.Permissions != null && kv.Value.Permissions.Any(p => epPerms.Any(epk => string.Equals(epk, p, StringComparison.OrdinalIgnoreCase))))
                .Select(kv => kv.Key)
                .ToArray();

            list.Add(new RbacEntry(endpointKey, allowedRoles, epPerms?.ToArray() ?? Array.Empty<string>(), ep.HttpMethod));
        }

        return list;
    }

    public static async Task<IResult> GetRbacEndpoint(IRoleManagementService service, CancellationToken cancellationToken)
    {
        try
        {
            var data = await BuildRbacAsync(service, cancellationToken);
            return Results.Ok(data);
        }
        catch (Exception ex)
        {
            return Results.Problem(title: "Failed to build RBAC matrix", detail: ex.Message);
        }
    }
}