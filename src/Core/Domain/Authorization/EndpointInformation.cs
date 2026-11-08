namespace UserOrganizationManagement.Domain.Authorization;

public record struct EndpointInformation(string Method, string PathRegex)
{
    public IEnumerable<string> PermissionTypes { get; set; } = [];
    public EndpointInformation(string method, string pathRegex, IEnumerable<string> permissionTypes) : this(method, pathRegex)
    {
        PermissionTypes = (permissionTypes ?? []).ToList();
    }
}