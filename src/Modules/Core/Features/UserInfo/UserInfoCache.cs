using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Http;

namespace Core.Features.UserInfo;

/// <summary>
/// Output-cache strategy for the UserInfo endpoints. The cache key varies by <c>sub</c> and a hash of
/// the caller's effective role and <c>projectRoles</c> claim, so a role/permission change produces a
/// different internal token, a different key, and is reflected immediately. Tagged by
/// <c>userinfo:{sub}</c> so profile changes evict precisely via domain events. The vary-by is derived
/// from the validated token claims (ADR-0006) — no role-service round-trip at cache time.
/// </summary>
internal static class UserInfoCache
{
    internal const string PolicyName = "UserInfo";

    internal static async ValueTask<CacheDescriptor?> DescribeAsync(HttpContext ctx, CancellationToken ct)
    {
        var caller = await Caller.BindAsync(ctx);
        if (caller is null)
        {
            return null;
        }

        var version = UserInfoCacheHelpers.BuildClaimsVersion(caller);

        return new CacheDescriptor(
            TimeSpan.FromMinutes(2),
            new Dictionary<string, string> { ["sub"] = caller.IdpId.Value, ["claims"] = version },
            [CacheTags.UserInfo(caller.IdpId.Value)]);
    }
}