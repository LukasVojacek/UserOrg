using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;

namespace UserOrg.Shared;

/// <summary>
/// Provides extension methods for authentication-related operations on HTTP contexts.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Attempts to retrieve the user identifier assigned by the identity provider from the current HTTP context when
    /// authenticated via JWT bearer token.
    /// </summary>
    /// <remarks>This method checks for the user identifier in the 'sub' claim or, if not present, the
    /// 'NameIdentifier' claim, but only returns a value if the authentication type is JWT bearer token.</remarks>
    /// <param name="context">The HTTP context containing the user principal and authentication information. Cannot be null.</param>
    /// <param name="identityProviderUserId">When this method returns, contains the user identifier from the identity provider if the user is authenticated
    /// with a JWT bearer token; otherwise, null.</param>
    /// <returns>true if the user is authenticated with a JWT bearer token and the identity provider user identifier was found;
    /// otherwise, false.</returns>
    public static bool TryGetUserIdentityProviderId(this HttpContext context, out string? identityProviderUserId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subClaimValue = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var authType = context.User.FindFirstValue(EAuthorizationMethod.ClaimType);
        var isBearerAuth = authType?.Equals(nameof(EAuthorizationMethod.JwtBearerToken), StringComparison.OrdinalIgnoreCase) ?? false;

        identityProviderUserId = isBearerAuth
            ? subClaimValue!
            : default;

        return identityProviderUserId is not null;
    }

    /// <summary>
    /// Reads the caller's identity directly from the claims on the current HTTP context.
    /// </summary>
    /// <remarks>Provider scoping and platform-admin precedence are the caller's responsibility (ADR-0080 §3);
    /// this method only surfaces the raw claim state, it does not authorize.</remarks>
    /// <param name="context">The HTTP context containing the user principal. Cannot be null.</param>
    /// <returns>A <see cref="CallerInfo"/> projecting the identity-provider user id, <c>provider_id</c> claim, and platform-admin role claim.</returns>
    public static CallerInfo GetCallerInfo(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.TryGetUserIdentityProviderId(out var identityProviderUserId);

        var providerId = context.User.FindFirstValue(AuthConstants.ProviderIdClaimType) is string providerIdClaimValue
            && Guid.TryParse(providerIdClaimValue, out var parsedProviderId)
                ? parsedProviderId
                : (Guid?)null;

        // Not IsInRole: the GatewayJwt handler sets RoleClaimType to "role", but the role claim is
        // materialized under ClaimTypes.Role (TokenService.cs / ApiKeyAuthenticationHandler.cs /
        // ServiceExtensions.cs), so IsInRole would never match. Read ClaimTypes.Role directly, same as
        // Core's TryGetIsAdmin.
        var isPlatformAdmin = string.Equals(
            context.User.FindFirstValue(ClaimTypes.Role),
            RoleInfoConstants.PlatformAdministratorRoleName,
            StringComparison.OrdinalIgnoreCase);

        return new CallerInfo(identityProviderUserId, providerId, isPlatformAdmin);
    }
}