using Lukdrasil.Error;

namespace Forge.Core.Features.EndpointMap;

internal static class EndpointPermissionErrors
{
    public static readonly Error InvalidEndpointName = new("InvalidEndpointName", ErrorSeverity.Error, "Endpoint name must be provided.");
    public static Error EndpointNotFound(string endpointName) => new("EndpointNotFound", ErrorSeverity.Error, $"Endpoint '{endpointName}' does not exist.");
    public static Error EndpointAlreadyExists(string endpointName) => new("EndpointAlreadyExists", ErrorSeverity.Error, $"Endpoint '{endpointName}' already exists.");

    public static readonly Error PermissionsNotFound = new(
            "PermissionsNotFound",
            ErrorSeverity.Error,
            "One or more requested permissions do not exist in the system.");

    public static readonly Error InvalidPathTemplate = new("InvalidPathTemplate", ErrorSeverity.Error, "Path template must be provided.");
    public static readonly Error InvalidHttpMethod = new("InvalidHttpMethod", ErrorSeverity.Error, "HTTP method must be provided.");
}