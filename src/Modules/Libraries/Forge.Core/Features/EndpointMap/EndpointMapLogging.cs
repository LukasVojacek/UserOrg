using Microsoft.Extensions.Logging;

namespace Forge.Core.Features.EndpointMap;

internal static partial class EndpointMapEndpointLogging
{
    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "Failed to set endpoint permissions for endpoint {EndpointName}: Permissions not found: {MissingPermissions}")]
    public static partial void PermissionsNotFound(this ILogger logger, string endpointName, string missingPermissions);

    [LoggerMessage(EventId = 1410, Level = LogLevel.Information, Message = "Exported {Count} endpoint mappings")]
    public static partial void ExportedEndpointMappings(this ILogger logger, int count);

    [LoggerMessage(EventId = 1411, Level = LogLevel.Debug, Message = "Updated endpoint mapping: {EndpointName}")]
    public static partial void UpdatedEndpointMapping(this ILogger logger, string endpointName);

    [LoggerMessage(EventId = 1412, Level = LogLevel.Debug, Message = "Skipping existing endpoint mapping: {EndpointName}")]
    public static partial void SkippingExistingEndpointMapping(this ILogger logger, string endpointName);

    [LoggerMessage(EventId = 1413, Level = LogLevel.Debug, Message = "Created endpoint mapping: {EndpointName}")]
    public static partial void CreatedEndpointMapping(this ILogger logger, string endpointName);

    [LoggerMessage(EventId = 1414, Level = LogLevel.Warning, Message = "Failed to import individual endpoint mapping item")]
    public static partial void FailedImportEndpointMappingItem(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1415, Level = LogLevel.Information, Message = "Imported endpoint mappings: {Created} created, {Updated} updated, {Errors} errors")]
    public static partial void ImportedEndpointMappings(this ILogger logger, int created, int updated, int errors);

    [LoggerMessage(EventId = 1416, Level = LogLevel.Error, Message = "Failed to import endpoint mappings")]
    public static partial void FailedImportEndpointMappings(this ILogger logger, Exception exception);
}