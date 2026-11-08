using Microsoft.Extensions.Logging;

namespace Forge.Core.Features.Role;

internal static partial class RoleLogging
{
    [LoggerMessage(EventId = 1200, Level = LogLevel.Error, Message = "Role {Role} was not persisted to the database due to a database update error.")]
    public static partial void RolePersistError(this ILogger logger, string role, Exception exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Warning, Message = "Role permission update failed for {Role}: missing permissions {MissingPermissions}")]
    public static partial void RolePermissionUpdateFailed(this ILogger logger, string role, string missingPermissions);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Updated permissions for role {Role}")]
    public static partial void UpdatedRolePermissions(this ILogger logger, string role);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error, Message = "Error saving state change for role '{Role}' to {Status}")]
    public static partial void ErrorSavingRoleState(this ILogger logger, string role, string status, Exception exception);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Information, Message = "Role '{Role}' status changed to {Status}")]
    public static partial void RoleStatusChanged(this ILogger logger, string role, string status);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Warning, Message = "Invalid role level {Level} provided for role '{Role}'")]
    public static partial void InvalidRoleLevelProvided(this ILogger logger, string role, int level);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Error, Message = "Error saving level change for role '{Role}' to {Level}")]
    public static partial void ErrorSavingRoleLevel(this ILogger logger, string role, int level, Exception exception);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Information, Message = "Role '{Role}' level changed to {Level}")]
    public static partial void RoleLevelChanged(this ILogger logger, string role, int level);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Error, Message = "Error saving category change for role '{Role}' to {Category}")]
    public static partial void ErrorSavingRoleCategory(this ILogger logger, string role, string category, Exception exception);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Information, Message = "Role '{Role}' category changed to {Category}")]
    public static partial void RoleCategoryChanged(this ILogger logger, string role, string category);

    [LoggerMessage(EventId = 1210, Level = LogLevel.Information, Message = "Exported {Count} roles")]
    public static partial void ExportedRoles(this ILogger logger, int count);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Debug, Message = "Updated role: {Name}")]
    public static partial void UpdatedRole(this ILogger logger, string name);

    [LoggerMessage(EventId = 1212, Level = LogLevel.Debug, Message = "Skipping existing role: {Name}")]
    public static partial void SkippingExistingRole(this ILogger logger, string name);

    [LoggerMessage(EventId = 1213, Level = LogLevel.Debug, Message = "Created role: {Name}")]
    public static partial void CreatedRole(this ILogger logger, string name);

    [LoggerMessage(EventId = 1214, Level = LogLevel.Warning, Message = "Failed to import individual role item")]
    public static partial void FailedImportRoleItem(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1215, Level = LogLevel.Information, Message = "Imported roles: {Created} created, {Updated} updated, {Errors} errors")]
    public static partial void ImportedRoles(this ILogger logger, int created, int updated, int errors);

    [LoggerMessage(EventId = 1216, Level = LogLevel.Error, Message = "Failed to import roles")]
    public static partial void FailedImportRoles(this ILogger logger, Exception exception);
}