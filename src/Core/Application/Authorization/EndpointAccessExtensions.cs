using UserOrganizationManagement.Domain.Authorization;
using UserOrganizationManagement.Domain.Exceptions;

namespace UserOrganizationManagement.Application.Authorization;

public static class EndpointAccessExtensions
{
    public static IReadOnlyList<string> ForHttpMethod(this PermissionFor permissionFor, string method)
    {
        EndpointInformation info = permissionFor.Endpoints.FirstOrDefault(x => x.Method == method);
        return info == default
            ? throw new EndpointPermissionNotFoundException($"""Endpoint "[{method}]{permissionFor.Endpoints.First().PathRegex}" was not configured. """)
            : (IReadOnlyList<string>)(info.PermissionTypes ?? []).ToList().AsReadOnly();
    }
}