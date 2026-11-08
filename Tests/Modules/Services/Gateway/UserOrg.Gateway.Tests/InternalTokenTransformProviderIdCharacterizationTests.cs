using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins what a Bearer caller can obtain in <c>provider_id</c> — the claim that grants kill-switch authority
/// (<c>KillSwitchAdminHandler.CanManage</c>) and, resolved to <c>provider_name</c>, decides whose data the
/// caller reads. <see cref="InternalTokenTransformExtensions.AddInternalTokenTransform"/> never derives one on
/// the <see cref="EAuthorizationMethod.JwtBearerToken"/> branch, and since D5 an inbound one no longer reaches
/// it either: the Gateway discards self-asserted <c>provider_id</c> at ingress.
/// No <see cref="Forge.Core.Features.EndpointMap.Services.IEndpointPermissionService"/> is registered,
/// so <see cref="AuthorizationHelpers.AuthorizeRequestAsync"/> short-circuits to authorized.
/// </summary>
public sealed class InternalTokenTransformProviderIdCharacterizationTests
{
    [Fact]
    public async Task Should_NotMintProviderId_OnJwtBearerTokenBranch_When_CallerHasNoProviderIdClaim()
    {
        var mintedProviderId = await CaptureMintedProviderIdAsync(BuildInteractiveHumanUser(providerIdClaim: null));

        Assert.Null(mintedProviderId);
    }

    [Fact]
    public async Task Should_NotMintProviderId_OnJwtBearerTokenBranch_When_CallerIsProviderAdminWithNoProviderIdClaim()
    {
        // A human ProviderAdmin is exactly the case a mis-implemented D5 (mint provider_id from the
        // effective role) would target — see KillSwitchAdminHandler.CanManage, which grants write
        // authority on provider_id alone.
        var mintedProviderId = await CaptureMintedProviderIdAsync(
            BuildInteractiveHumanUser(providerIdClaim: null),
            effectiveRoleClaim: RoleInfoConstants.ProviderAdministratorRoleName);

        Assert.Null(mintedProviderId);
    }

    /// <summary>
    /// The inverted characterization: until D5 the transform <b>relayed</b> an incoming <c>provider_id</c>
    /// verbatim, with no auth-method gate, and this case pinned that so the two above could not be misread as
    /// "an interactive caller never gets provider_id". The relay was removed on purpose — an IdP-issued claim
    /// cannot be trusted to say which provider a caller acts for, the same argument that made the Gateway's
    /// <c>am</c> authoritative. The forged claim must therefore be gone by the time the transform reads the
    /// principal, which is why this case runs the real ingress instead of a hand-built principal.
    /// </summary>
    [Fact]
    public async Task Should_DiscardForgedProviderId_OnJwtBearerTokenBranch_When_CallerCarriesTheClaim()
    {
        const string forgedProviderId = "0f9d5a1c-7c2e-4a58-8f3b-2c1d9e6b4a70";

        var mintedProviderId = await CaptureMintedProviderIdAsync(await GatewayAuthenticatedUserAsync(forgedProviderId));

        Assert.Null(mintedProviderId);
    }

    private static async Task<string?> CaptureMintedProviderIdAsync(ClaimsPrincipal user, string effectiveRoleClaim = "User")
    {
        var tokenProvider = new CapturingInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti("internal-jti") };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddSingleton<IInternalTokenProvider>(tokenProvider);
        services.AddSingleton<IUserPermissionsCache>(new StubPermissionsCache(effectiveRoleClaim));
        using var serviceProvider = services.BuildServiceProvider();

        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, serviceProvider, "Bearer incoming-user-token");

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.True(tokenProvider.GenerateInternalTokenCalled);
        return tokenProvider.LastProviderId;
    }

    private static ClaimsPrincipal BuildInteractiveHumanUser(string? providerIdClaim)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "human-1"),
            new(JwtRegisteredClaimNames.PreferredUsername, "human-1"),
            new(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken)),
            new(ClaimTypes.Role, "User")
        };
        if (providerIdClaim is not null)
        {
            claims.Add(new Claim(AuthConstants.ProviderIdClaimType, providerIdClaim));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    /// <summary>
    /// The principal a real Bearer request reaches the transform with: an IdP token's claims run through the
    /// Gateway's own <c>OnTokenValidated</c> handler, which is where a self-asserted claim is discarded.
    /// </summary>
    private static async Task<ClaimsPrincipal> GatewayAuthenticatedUserAsync(string forgedProviderIdClaim)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = BuildInteractiveHumanUser(forgedProviderIdClaim)
        };

        await ServicesExtensions.CreateJwtBearerEvents("https://idp.test").OnTokenValidated(context);

        return context.Principal!;
    }

    private sealed class StubPermissionsCache(string effectiveRoleClaim) : IUserPermissionsCache
    {
        public Task<UserPermissionsSnapshot> GetAsync(string idpUserId, CancellationToken ct)
            => Task.FromResult(new UserPermissionsSnapshot { EffectiveRoleClaim = effectiveRoleClaim });
        public Task InvalidateAsync(string idpUserId, CancellationToken ct)
            => Task.CompletedTask;

        public Task InvalidateAllAsync(CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class CapturingInternalTokenProvider : IInternalTokenProvider
    {
        public string? InternalToken { get; init; }

        public bool GenerateInternalTokenCalled { get; private set; }

        public string? LastProviderId { get; private set; }

        public Task<string?> GenerateInternalTokenAsync(EAuthorizationMethod authMethod, string subject, string username, string systemRole, string? providerId, string? email = null, IDictionary<string, ICollection<string>>? projectRoles = null, string? providerName = null, string? originalJti = null)
        {
            GenerateInternalTokenCalled = true;
            LastProviderId = providerId;
            return Task.FromResult(InternalToken);
        }

        public Task<string?> GenerateServiceToServiceTokenAsync(string originService, string targetAudience = "forge")
            => throw new NotSupportedException();

        public Task<ClaimsPrincipal?> ValidateInternalTokenAsync(string token)
            => throw new NotSupportedException();

        public string GetCurrentKeyId()
            => throw new NotSupportedException();
    }
}
