using Lukdrasil.Error;

namespace Forge.Core.Features.Role;

public static class RoleErrors
{
    public static readonly Error InvalidRoleName = new(
        "InvalidRoleName",
        ErrorSeverity.Error,
        "The provided role name is invalid.");

    public static readonly Error InvalidCategory = new(
        "InvalidCategory",
        ErrorSeverity.Error,
        "The provided role category is invalid.");

    public static Error RoleAlreadyExists(string roleName) => new(
        "RoleAlreadyExists",
        ErrorSeverity.Error,
        $"Role '{roleName}' already exists.");

    internal static Error DatabaseUpdateError(string roleName) => new(
        "DatabaseUpdateError",
        ErrorSeverity.Error,
        $"An error occurred while updating the database for role '{roleName}'.");
    internal static Error PermissionsNotFound(List<string> missing) => new(
        "PermissionsNotFound",
        ErrorSeverity.Error,
        $"The following permissions do not exist: {string.Join(", ", missing)}");

    internal static Error RoleNotFound(string roleName) => new(
        "RoleNotFound",
        ErrorSeverity.Error,
        $"Role '{roleName}' does not exist.");

    internal static Error RoleNotActive(string roleName) => new(
        "RoleNotActive",
        ErrorSeverity.Error,
        $"Role '{roleName}' is not active.");

    public static readonly Error InvalidCategoryName = new(
        "InvalidCategory",
        ErrorSeverity.Error,
        "The category name is invalid.");
}