using Microsoft.Extensions.Logging;

namespace Forge.Core.Features.Permission;

internal static partial class PermissionLogging
{
    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Exported {Count} permissions")]
    public static partial void ExportedPermissions(this ILogger logger, int count);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "Updated permission: {Key}")]
    public static partial void UpdatedPermission(this ILogger logger, string key);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Debug, Message = "Skipping existing permission: {Key}")]
    public static partial void SkippingExistingPermission(this ILogger logger, string key);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Debug, Message = "Created permission: {Key}")]
    public static partial void CreatedPermission(this ILogger logger, string key);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Failed to import individual permission item")]
    public static partial void FailedImportPermissionItem(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Information, Message = "Imported permissions: {Created} created, {Updated} updated, {Errors} errors")]
    public static partial void ImportedPermissions(this ILogger logger, int created, int updated, int errors);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Error, Message = "Failed to import permissions")]
    public static partial void FailedImportPermissions(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Debug, Message = "Deleted permission: {Key}")]
    public static partial void DeletedPermission(this ILogger logger, string key);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Warning, Message = "Permission {Key} not deleted: assigned to roles")]
    public static partial void PermissionDeleteRefusedAssigned(this ILogger logger, string key);
}