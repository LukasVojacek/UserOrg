using Lukdrasil.Error;

using UserOrg.Shared;

namespace Forge.Core.Features.Permission;

public static class PermissionErrors
{

    public static readonly Error InvalidPermissionKey = new(
            "InvalidPermissionKey",
            ErrorSeverity.Error,
            "The provided permission key is invalid.");

    public static Error PermissionAlreadyExists(PermissionKey key) => new(
            "PermissionAlreadyExists",
            ErrorSeverity.Error,
            $"Permission '{key.Value}' already exists in the system.");

    public static Error PermissionNotFound(PermissionKey key) => new(
            "PermissionNotFound",
            ErrorSeverity.Error,
            $"Permission '{key.Value}' does not exist in the system.");

    public static Error PermissionAssignedToRoles(PermissionKey key) => new(
            "PermissionAssignedToRoles",
            ErrorSeverity.Error,
            $"Permission '{key.Value}' is assigned to one or more roles and cannot be deleted.");
}