using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Forge.Core.Features.UserOrgRoles;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Xunit;

using Yarp.ReverseProxy.Transforms;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Covers D5: an interactive human ProviderAdmin is minted the additive <c>provider_name</c> claim, so they
/// resolve to their own provider's read scope instead of an empty one. The predicate is <b>role AND
/// assignment</b> — the effective role from the permissions snapshot must be
/// <see cref="RoleInfoConstants.ProviderAdministratorRoleName"/> <i>and</i> the user must still have a provider
/// assigned — and the mint sits inside the <see cref="EAuthorizationMethod.JwtBearerToken"/> branch, so it can
/// never be reached by a caller who merely claims to be one. <c>provider_id</c> stays untouched: it is what
/// grants kill-switch authority (<c>KillSwitchAdminHandler.CanManage</c>), so the Gateway must never mint one.
/// No <see cref="Forge.Core.Features.EndpointMap.Services.IEndpointPermissionService"/> is registered, so
/// <see cref="AuthorizationHelpers.AuthorizeRequestAsync"/> short-circuits to authorized.
/// </summary>
public sealed class InternalTokenTransformProviderNameMintTests
{
    private const string AssignedProvider = "acme";
    private const string HumanSubject = "human-1";
    private const string OtherAdministrator = "human-2";
    private const string OtherAdministratorsProvider = "globex";
    private const string ServiceKeyProvider = "service-key-provider";

    [Fact]
    public async Task Should_MintProviderName_When_RoleIsProviderAdmin_AndAssignmentPresent()
    {
        var tokenProvider = await MintAsync(
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider);

        Assert.Equal(AssignedProvider, tokenProvider.LastProviderName);
    }

    [Fact]
    public async Task Should_NotMint_When_RoleIsProviderAdmin_ButNoProviderAssigned()
    {
        var tokenProvider = await MintAsync(
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: null);

        Assert.Null(tokenProvider.LastProviderName);
    }

    /// <summary>
    /// The other half of the conjunction, and the one that decides whether a revoked administrator keeps
    /// provider-tier reads: the demotion left <c>User.AssignedProviderId</c> behind, so the lookup still
    /// resolves a provider. Only the effective role separates this caller from the minting one.
    /// </summary>
    [Fact]
    public async Task Should_NotMint_When_RoleIsProviderAdmin_ButAssignmentStale_AfterDemotion()
    {
        var tokenProvider = await MintAsync(
            effectiveRole: RoleInfoConstants.PlatformUserRoleName,
            assignedProviderName: AssignedProvider);

        Assert.Null(tokenProvider.LastProviderName);
    }

    /// <summary>
    /// The mint lives inside the JwtBearerToken branch, so the assignment lookup is unreachable for a service
    /// key: this one arrives with the authorization method its own handler stamps and no <c>provider_id</c>, and
    /// must be minted no provider claim even though the same user has a provider assigned in the database.
    /// </summary>
    [Fact]
    public async Task Should_NotMint_When_CallerIsServiceKeyIdentity()
    {
        var tokenProvider = new CapturingInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti("internal-jti") };

        await ApplyTransformAsync(
            tokenProvider,
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider,
            user: ApiKeyPrincipal());

        Assert.True(tokenProvider.GenerateInternalTokenCalled);
        Assert.Null(tokenProvider.LastProviderName);
    }

    /// <summary>
    /// Drives the real inbound path — the JwtBearer <c>OnTokenValidated</c> handler followed by the transform —
    /// because a hand-built principal cannot show which <c>am</c> wins. A forged <c>am=ApiKey</c> must buy
    /// nothing: the caller is minted exactly what an honest interactive ProviderAdmin is minted, with the
    /// Gateway's own authorization method (which is what Core's service-key-only write gate reads) and no
    /// <c>provider_id</c> (which is what grants kill-switch authority).
    /// </summary>
    [Fact]
    public async Task Should_MintNothingExtra_When_AmClaimForged_AsApiKey()
    {
        var tokenProvider = await MintAsync(
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider,
            inboundAuthMethod: nameof(EAuthorizationMethod.ApiKey));

        Assert.Equal(EAuthorizationMethod.JwtBearerToken, tokenProvider.LastAuthMethod);
        Assert.Null(tokenProvider.LastProviderId);
        Assert.Equal(AssignedProvider, tokenProvider.LastProviderName);
    }

    /// <summary>
    /// The lookup must be made for the calling subject and no other. Both administrators are assigned in the
    /// resolver, so a mint that read a different identifier (a hard-coded id, or <c>preferred_username</c>
    /// instead of <c>sub</c> — the two differ here) resolves the wrong provider's name rather than nothing.
    /// </summary>
    [Fact]
    public async Task Should_MintTheCallersOwnProvider_When_AnotherAdministratorHasADifferentProvider()
    {
        var tokenProvider = await MintAsync(
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider,
            subject: OtherAdministrator);

        Assert.Equal(OtherAdministratorsProvider, tokenProvider.LastProviderName);
    }

    /// <summary>
    /// The service-key resolution below the mint resolves <c>provider_id</c> to a name, and must not run for an
    /// interactive caller: a <c>provider_id</c> that reaches the transform on the Bearer path is self-asserted,
    /// so resolving it would hand the caller a provider of their own choosing. Today ingress discards that claim
    /// (<c>ServicesExtensions.TryDiscardSelfAssertedIdentityClaims</c>) and this principal is hand-built to
    /// bypass it, so the invariant is pinned where the code that depends on it lives rather than two files away.
    /// </summary>
    [Fact]
    public async Task Should_KeepTheMintedProviderName_When_ABearerPrincipalStillCarriesProviderId()
    {
        var tokenProvider = new CapturingInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti("internal-jti") };

        await ApplyTransformAsync(
            tokenProvider,
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider,
            user: BearerPrincipalCarryingProviderId(Guid.NewGuid()));

        Assert.Equal(AssignedProvider, tokenProvider.LastProviderName);
    }

    /// <summary>
    /// The other side of that gate: a provider service key still has its <c>provider_id</c> resolved to the
    /// provider's natural name (ADR-0011), which is what Core scopes provider and location writes by.
    /// </summary>
    [Fact]
    public async Task Should_ResolveProviderNameFromProviderId_When_CallerIsServiceKeyIdentity()
    {
        var providerId = Guid.NewGuid();
        var tokenProvider = new CapturingInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti("internal-jti") };

        await ApplyTransformAsync(
            tokenProvider,
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider,
            user: ApiKeyPrincipal(providerId));

        Assert.Equal(ServiceKeyProvider, tokenProvider.LastProviderName);
        Assert.Equal(providerId.ToString(), tokenProvider.LastProviderId);
    }

    /// <summary>
    /// The mint must not spill into <c>provider_id</c>. Asserting <c>provider_name</c> alongside it keeps the
    /// case honest: the absence below is from a configuration that really did mint, not from a dead branch.
    /// </summary>
    [Fact]
    public async Task Should_NotMintProviderId_ForInteractiveCaller()
    {
        var tokenProvider = await MintAsync(
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider);

        Assert.Null(tokenProvider.LastProviderId);
        Assert.Equal(AssignedProvider, tokenProvider.LastProviderName);
    }

    /// <summary>
    /// The wire half of the claim chain: the same run through the real <see cref="TokenService"/>, asserting the
    /// claims the API will actually receive. Core binds its caller from these names, so a mint that never
    /// reached the token would be invisible to the stub-based cases above. The other half of the chain is
    /// <c>Core.UnitTests.Auth.HumanProviderAdminClaimChainTests</c>, which binds this exact claim set with the
    /// real <c>Caller.BindAsync</c>; the two meet at the claim names asserted below.
    /// </summary>
    [Fact]
    public async Task Should_CarryProviderNameOnTheMintedToken_When_HumanProviderAdmin()
    {
        using var tokenServices = await InternalTokenTestServices.BuildStartedAsync();
        var realTokenProvider = tokenServices.GetRequiredService<IInternalTokenProvider>();

        var requestContext = await ApplyTransformAsync(
            realTokenProvider,
            effectiveRole: RoleInfoConstants.ProviderAdministratorRoleName,
            assignedProviderName: AssignedProvider,
            await AuthenticatedBearerPrincipalAsync(inboundAuthMethod: null));

        var minted = ReadMintedToken(requestContext);
        Assert.Equal(AssignedProvider, ClaimValue(minted, "provider_name"));
        Assert.Equal(nameof(EAuthorizationMethod.JwtBearerToken), ClaimValue(minted, "am"));
        // The mint always emits provider_id; for an interactive caller it is empty, and an empty value can
        // never parse into the Guid KillSwitchAdminHandler compares a caller's provider against.
        Assert.Equal(string.Empty, ClaimValue(minted, "provider_id"));
    }

    private static async Task<CapturingInternalTokenProvider> MintAsync(
        string effectiveRole,
        string? assignedProviderName,
        string? inboundAuthMethod = null,
        string subject = HumanSubject)
    {
        var tokenProvider = new CapturingInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti("internal-jti") };

        await ApplyTransformAsync(
            tokenProvider,
            effectiveRole,
            assignedProviderName,
            await AuthenticatedBearerPrincipalAsync(inboundAuthMethod, subject));

        Assert.True(tokenProvider.GenerateInternalTokenCalled);
        return tokenProvider;
    }

    private static async Task<RequestTransformContext> ApplyTransformAsync(
        IInternalTokenProvider tokenProvider,
        string effectiveRole,
        string? assignedProviderName,
        ClaimsPrincipal user)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddSingleton(tokenProvider);
        services.AddSingleton<IUserPermissionsCache>(new StubPermissionsCache(effectiveRole));
        services.AddSingleton<IProviderIdentityResolver>(new StubProviderIdentityResolver(assignedProviderName));
        using var serviceProvider = services.BuildServiceProvider();

        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, serviceProvider, "Bearer incoming-user-token");

        return await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);
    }

    /// <summary>
    /// Runs an IdP token's claims through the Gateway's own <c>OnTokenValidated</c> handler, the way a real
    /// Bearer request does, so the principal the transform sees is the one the Gateway produced rather than one
    /// the test wrote — the only way to observe which <c>am</c> a forging token ends up with.
    /// </summary>
    private static async Task<ClaimsPrincipal> AuthenticatedBearerPrincipalAsync(string? inboundAuthMethod, string subject = HumanSubject)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.PreferredUsername, $"{subject}@idp.test")
        };
        if (inboundAuthMethod is not null)
        {
            claims.Add(new Claim(EAuthorizationMethod.ClaimType, inboundAuthMethod));
        }

        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"))
        };

        await ServicesExtensions.CreateJwtBearerEvents("https://idp.test").OnTokenValidated(context);

        return context.Principal!;
    }

    /// <summary>The shape <c>ApiKeyAuthenticationHandler</c> builds from scratch for a service key.</summary>
    private static ClaimsPrincipal ApiKeyPrincipal(Guid? providerId = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, HumanSubject),
            new(JwtRegisteredClaimNames.PreferredUsername, HumanSubject),
            new(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey)),
            new(ClaimTypes.Role, "Provider")
        };
        if (providerId is not null)
        {
            claims.Add(new Claim(AuthConstants.ProviderIdClaimType, providerId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    /// <summary>
    /// An interactive principal that reached the transform with a <c>provider_id</c> claim — the shape ingress
    /// discards today, hand-built so the transform's own gate is what the assertion depends on.
    /// </summary>
    private static ClaimsPrincipal BearerPrincipalCarryingProviderId(Guid providerId) =>
        new(new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, HumanSubject),
                new Claim(JwtRegisteredClaimNames.PreferredUsername, $"{HumanSubject}@idp.test"),
                new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken)),
                new Claim(AuthConstants.ProviderIdClaimType, providerId.ToString())
            ],
            "TestAuthType"));

    private static JwtSecurityToken ReadMintedToken(RequestTransformContext requestContext)
    {
        var authorization = requestContext.ProxyRequest.Headers.Authorization;
        Assert.NotNull(authorization);
        return new JwtSecurityTokenHandler().ReadJwtToken(authorization.Parameter);
    }

    // The claim names are spelled out rather than taken from AuthConstants on purpose: they are a
    // cross-process wire contract, so a rename of a constant's value must fail this test.
    private static string? ClaimValue(JwtSecurityToken token, string claimType)
        => token.Claims.SingleOrDefault(claim => claim.Type == claimType)?.Value;

    private sealed class StubPermissionsCache(string effectiveRoleClaim) : IUserPermissionsCache
    {
        public Task<UserPermissionsSnapshot> GetAsync(string idpUserId, CancellationToken ct)
            => Task.FromResult(new UserPermissionsSnapshot { EffectiveRoleClaim = effectiveRoleClaim });
        public Task InvalidateAsync(string idpUserId, CancellationToken ct)
            => Task.CompletedTask;

        public Task InvalidateAllAsync(CancellationToken ct)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Keyed on the caller's subject, and it always knows a second administrator of a different provider: a
    /// lookup made for anyone but the calling subject then resolves the <i>wrong</i> name instead of nothing,
    /// which is what makes the subject binding falsifiable.
    /// </summary>
    private sealed class StubProviderIdentityResolver(string? assignedProviderName) : IProviderIdentityResolver
    {
        private readonly Dictionary<string, string?> _assignedProviderNames = new()
        {
            [HumanSubject] = assignedProviderName,
            [OtherAdministrator] = OtherAdministratorsProvider
        };

        public Task<string?> GetProviderNameAsync(Guid providerId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(ServiceKeyProvider);

        public Task<string?> GetAssignedProviderNameAsync(string idpUserId, CancellationToken cancellationToken)
            => Task.FromResult(_assignedProviderNames.GetValueOrDefault(idpUserId));
    }

    private sealed class CapturingInternalTokenProvider : IInternalTokenProvider
    {
        public string? InternalToken { get; init; }

        public bool GenerateInternalTokenCalled { get; private set; }

        public EAuthorizationMethod LastAuthMethod { get; private set; }

        public string? LastProviderId { get; private set; }

        public string? LastProviderName { get; private set; }

        public Task<string?> GenerateInternalTokenAsync(EAuthorizationMethod authMethod, string subject, string username, string systemRole, string? providerId, string? email = null, IDictionary<string, ICollection<string>>? projectRoles = null, string? providerName = null, string? originalJti = null)
        {
            GenerateInternalTokenCalled = true;
            LastAuthMethod = authMethod;
            LastProviderId = providerId;
            LastProviderName = providerName;
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
