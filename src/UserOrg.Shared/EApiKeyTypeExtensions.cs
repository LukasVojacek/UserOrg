using System.Security.Claims;

namespace UserOrg.Shared;

public static class EApiKeyTypeExtensions
{
    extension(EApiKeyType apiKeyType)
    {
        public bool IsLegacy => apiKeyType is EApiKeyType.LegacyService or EApiKeyType.LegacyProvider;

        public bool IsInRole(ClaimsPrincipal claimsPrincipal) => claimsPrincipal?.IsInRole(apiKeyType.ToString()) ?? false;
    }
}