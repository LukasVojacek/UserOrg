using Forge.Core.Features.Role.Services;

namespace Core.Shared;

internal static class RoleLookup
{
    extension(IList<RoleInfo> roles)
    {
        internal RoleInfo? FindByName(string name) =>
            roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}