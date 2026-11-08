using System.Security.Cryptography;
using System.Text;

namespace Core.Features.UserInfo;

internal static class UserInfoCacheHelpers
{
    /// <summary>
    /// Stable hash of the caller's effective role and <c>projectRoles</c> claim, used as the UserInfo
    /// output-cache vary-by key. Ordering is normalised so the same access always hashes identically.
    /// </summary>
    internal static string BuildClaimsVersion(Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        var projectRoles = string.Concat(
            caller.ProjectRoles
                .OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => $"{r.Key}:{string.Join(",", r.Value.Select(p => p.Value).OrderBy(p => p, StringComparer.Ordinal))}"));

        var input = $"{caller.Role}|{projectRoles}";

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}