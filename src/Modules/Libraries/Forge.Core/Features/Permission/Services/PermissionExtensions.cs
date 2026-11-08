namespace Forge.Core.Features.Permission.Services;

internal static class PermissionExtensions
{
    extension(Permission.Persistence.Permission permission)
    {
        public PermissionInfo ToPermissionInfo() => new(permission.Key, permission.Description ?? "");
    }
}