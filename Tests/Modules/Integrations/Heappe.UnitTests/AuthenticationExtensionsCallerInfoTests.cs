using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

/// <summary>
/// Pins the raw claim projection read by <see cref="AuthenticationExtensions.GetCallerInfo"/> (ADR-0080 §1):
/// the platform-admin short-circuit must win even when a provider_id claim is also present, since a real
/// platform admin never carries one (Core injects provider_id only for ProviderAdministrator).
/// </summary>
public sealed class AuthenticationExtensionsCallerInfoTests
{
    private static readonly Guid ProviderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact(DisplayName = "Should_set_ProviderId_and_not_admin_When_provider_claim_present_without_admin_role")]
    public void Should_set_ProviderId_and_not_admin_When_provider_claim_present_without_admin_role()
    {
        var context = HttpContextWith(
            new Claim(AuthConstants.ProviderIdClaimType, ProviderId.ToString()),
            new Claim(ClaimTypes.Role, RoleInfoConstants.ProviderAdministratorRoleName));

        var caller = context.GetCallerInfo();

        Assert.Equal(ProviderId, caller.ProviderId);
        Assert.False(caller.IsPlatformAdmin);
    }

    [Fact(DisplayName = "Should_set_IsPlatformAdmin_When_admin_role_present_without_provider_claim")]
    public void Should_set_IsPlatformAdmin_When_admin_role_present_without_provider_claim()
    {
        var context = GatewayJwtHttpContextWith(
            new Claim(ClaimTypes.Role, RoleInfoConstants.PlatformAdministratorRoleName));

        var caller = context.GetCallerInfo();

        Assert.True(caller.IsPlatformAdmin);
        Assert.Null(caller.ProviderId);
    }

    [Fact(DisplayName = "Should_set_IsPlatformAdmin_When_admin_role_present_alongside_provider_claim")]
    public void Should_set_IsPlatformAdmin_When_admin_role_present_alongside_provider_claim()
    {
        var context = GatewayJwtHttpContextWith(
            new Claim(AuthConstants.ProviderIdClaimType, ProviderId.ToString()),
            new Claim(ClaimTypes.Role, RoleInfoConstants.PlatformAdministratorRoleName));

        var caller = context.GetCallerInfo();

        Assert.True(caller.IsPlatformAdmin);
    }

    [Fact(DisplayName = "Should_return_null_ProviderId_and_not_admin_When_neither_claim_present")]
    public void Should_return_null_ProviderId_and_not_admin_When_neither_claim_present()
    {
        var context = HttpContextWith();

        var caller = context.GetCallerInfo();

        Assert.Null(caller.ProviderId);
        Assert.False(caller.IsPlatformAdmin);
    }

    [Fact(DisplayName = "Should_return_null_ProviderId_and_not_throw_When_provider_claim_is_malformed")]
    public void Should_return_null_ProviderId_and_not_throw_When_provider_claim_is_malformed()
    {
        var context = HttpContextWith(new Claim(AuthConstants.ProviderIdClaimType, "not-a-guid"));

        var caller = context.GetCallerInfo();

        Assert.Null(caller.ProviderId);
        Assert.False(caller.IsPlatformAdmin);
    }

    private static HttpContext HttpContextWith(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    /// <summary>
    /// Builds an identity matching the GatewayJwt host configuration: <c>RoleClaimType = "role"</c>
    /// (Core/API/Authentication/AuthenticationExtensions.cs) while the role claim itself is materialized
    /// under <see cref="ClaimTypes.Role"/> (TokenService.cs / ApiKeyAuthenticationHandler.cs /
    /// ServiceExtensions.cs). Under this real configuration, <c>ClaimsPrincipal.IsInRole</c> never matches
    /// — only reading <see cref="ClaimTypes.Role"/> directly does.
    /// </summary>
    private static HttpContext GatewayJwtHttpContextWith(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth", ClaimTypes.Name, roleType: "role");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }
}
