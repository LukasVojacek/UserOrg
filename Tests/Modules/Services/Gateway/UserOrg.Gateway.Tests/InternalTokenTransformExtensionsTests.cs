using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Forge.Core.Features.EndpointMap.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes <see cref="InternalTokenTransformExtensions.AddInternalTokenTransform"/> today: the
/// Bearer path, the missing-Authorization 401, and the public-endpoint S2S swap, plus the public-endpoint
/// path of <see cref="InternalTokenTransformExtensions.AddPermissionCheckTransform"/>. No
/// <see cref="Forge.Core.Features.EndpointMap.Services.IEndpointPermissionService"/> is registered in
/// these tests, so <see cref="AuthorizationHelpers.AuthorizeRequestAsync"/> short-circuits to
/// authorized and the transform's own logic is what's under test.
/// </summary>
public sealed class InternalTokenTransformExtensionsTests
{
    private const string OriginalAuthorizationHeader = "Bearer incoming-user-token";

    // Deliberately different from the principal's ClaimTypes.Role ("User") so the substitution the
    // transform performs is visible: a stub that echoed the principal's role would pass even if the
    // transform stopped substituting altogether.
    private const string StubEffectiveRole = "Admin";

    private const string MintedInternalTokenJti = "internal-jti-1";
    private const string S2STokenJti = "s2s-jti-1";

    private static ClaimsPrincipal BuildBearerUser(string? incomingJti = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "sub-1"),
            new(JwtRegisteredClaimNames.PreferredUsername, "user-1"),
            new(JwtRegisteredClaimNames.Email, "user-1@test.local"),
            new(AuthConstants.ProviderIdClaimType, "provider-1"),
            new(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken)),
            new(ClaimTypes.Role, "User")
        };
        if (incomingJti is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, incomingJti));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    /// <summary>
    /// An ApiKey principal is built from scratch by the Gateway's own handler — there is no incoming
    /// JWT and therefore no <c>jti</c> claim to relay.
    /// </summary>
    private static ClaimsPrincipal BuildApiKeyUser()
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "sub-key"),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, "user-key"),
            new Claim(AuthConstants.ProviderIdClaimType, "provider-1"),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey)),
            new Claim(ClaimTypes.Role, "User")
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
    }

    private static ClaimsPrincipal BuildPublicUser()
        => new(new ClaimsIdentity([new Claim(Constants.PublicAccessClaimType, "true")], "TestAuthType"));

    private static ServiceProvider BuildServices(IInternalTokenProvider tokenProvider, IDiagnosticContext? diagnosticContext = null, bool registerDiagnosticContext = true)
        => BuildServices(tokenProvider, logs: null, endpointPermissionService: null, diagnosticContext, registerDiagnosticContext);

    private static ServiceProvider BuildServices(
        IInternalTokenProvider tokenProvider,
        CapturingLoggerProvider? logs,
        IEndpointPermissionService? endpointPermissionService,
        IDiagnosticContext? diagnosticContext = null,
        bool registerDiagnosticContext = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace);
            }
        });
        if (endpointPermissionService is not null)
        {
            services.AddSingleton(endpointPermissionService);
        }
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddSingleton(tokenProvider);
        services.AddSingleton<IUserPermissionsCache>(new StubPermissionsCache());
        if (registerDiagnosticContext)
        {
            services.AddSingleton(diagnosticContext ?? Mock.Of<IDiagnosticContext>());
        }
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Bearer_SwapsAuthorizationForInternalTokenAndPreservesOriginal()
    {
        var internalToken = InternalTokenTransformTestHarness.WriteTokenWithJti(MintedInternalTokenJti);
        var tokenProvider = new StubInternalTokenProvider { InternalToken = internalToken };
        using var services = BuildServices(tokenProvider);
        var user = BuildBearerUser();
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services, OriginalAuthorizationHeader);

        var requestContext = await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Equal($"Bearer {internalToken}", requestContext.ProxyRequest.Headers.Authorization!.ToString());
        Assert.Equal(OriginalAuthorizationHeader, requestContext.ProxyRequest.Headers.GetValues("X-Original-Authorization").Single());

        Assert.Equal(EAuthorizationMethod.JwtBearerToken, tokenProvider.LastAuthMethod);
        Assert.Equal("sub-1", tokenProvider.LastSubject);
        Assert.Equal("user-1", tokenProvider.LastUsername);
        Assert.Equal("provider-1", tokenProvider.LastProviderId);

        // For JwtBearerToken the transform discards the principal's ClaimTypes.Role ("User", above)
        // and substitutes the permissions-cache snapshot's EffectiveRoleClaim before minting — that
        // substitution is a cache-key segment (systemRole), so it must be pinned here, not assumed.
        Assert.Equal(StubEffectiveRole, tokenProvider.LastSystemRole);
    }

    [Fact]
    public async Task Bearer_RelaysIncomingJtiToTheMintAndPublishesItAsTheChainTokenId()
    {
        const string incomingJti = "idp-jti-1";
        var tokenProvider = new StubInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti(MintedInternalTokenJti) };
        using var services = BuildServices(tokenProvider);
        var user = BuildBearerUser(incomingJti);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services, OriginalAuthorizationHeader);

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Equal(incomingJti, tokenProvider.LastOriginalJti);
        Assert.Equal(incomingJti, httpContext.Items[InternalTokenTransformExtensions.ChainTokenIdItemKey]);
    }

    [Fact]
    public async Task Bearer_WithEmptyIncomingJti_IsTreatedAsHavingNoIncomingJti()
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti(MintedInternalTokenJti) };
        using var services = BuildServices(tokenProvider);
        var user = BuildBearerUser(incomingJti: string.Empty);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services, OriginalAuthorizationHeader);

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        // An empty jti yields no original_jti claim on the minted token, so the Gateway must publish the
        // same fallback the downstream reader will compute — not an empty string.
        Assert.Null(tokenProvider.LastOriginalJti);
        Assert.Equal(MintedInternalTokenJti, httpContext.Items[InternalTokenTransformExtensions.ChainTokenIdItemKey]);
    }

    [Fact]
    public async Task ApiKey_MintsWithoutOriginalJtiAndPublishesTheInternalTokensOwnJti()
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti(MintedInternalTokenJti) };
        using var services = BuildServices(tokenProvider);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildApiKeyUser(), services, "ApiKey some-key");

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Null(tokenProvider.LastOriginalJti);
        Assert.Equal(MintedInternalTokenJti, httpContext.Items[InternalTokenTransformExtensions.ChainTokenIdItemKey]);
    }

    /// <summary>
    /// ApiKey has no incoming token, so unlike the Bearer case there is no <c>OnTokenValidated</c> handler to
    /// source <c>auth.jti</c> from — the mint site is the only place the internal token's own jti is known
    /// (criterion 3).
    /// </summary>
    [Fact]
    public async Task ApiKey_SetsDiagnosticContextToTheInternalTokensOwnJti()
    {
        var diagnosticContext = new Mock<IDiagnosticContext>();
        var tokenProvider = new StubInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti(MintedInternalTokenJti) };
        using var services = BuildServices(tokenProvider, diagnosticContext.Object);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildApiKeyUser(), services, "ApiKey some-key");

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        diagnosticContext.Verify(d => d.Set("auth.jti", MintedInternalTokenJti, false), Times.Once);
    }

    /// <summary>
    /// A missing logging side-channel must never break request forwarding. Verified with no
    /// <see cref="IDiagnosticContext"/> registered at all, not merely a null one, so a
    /// <see cref="ServiceProviderServiceExtensions.GetRequiredService{T}"/>-shaped regression is caught here.
    /// </summary>
    [Fact]
    public async Task ApiKey_WithNoDiagnosticContextRegistered_DoesNotThrow()
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti(MintedInternalTokenJti) };
        using var services = BuildServices(tokenProvider, registerDiagnosticContext: false);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildApiKeyUser(), services, "ApiKey some-key");

        var requestContext = await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Equal($"Bearer {tokenProvider.InternalToken}", requestContext.ProxyRequest.Headers.Authorization!.ToString());
        Assert.Equal(MintedInternalTokenJti, httpContext.Items[InternalTokenTransformExtensions.ChainTokenIdItemKey]);
    }

    [Fact]
    public async Task MissingAuthorizationHeader_ShortCircuitsWith401()
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = "unused" };
        using var services = BuildServices(tokenProvider);
        var user = BuildBearerUser();
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services, incomingAuthorization: null);
        using var body = new System.IO.MemoryStream();
        httpContext.Response.Body = body;

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Equal(ProblemResponse.ContentType, httpContext.Response.ContentType);
        Assert.Equal("Bearer", httpContext.Response.Headers.WWWAuthenticate.ToString());
        using var problem = System.Text.Json.JsonDocument.Parse(body.ToArray());
        Assert.Equal("auth.unauthorized", problem.RootElement.GetProperty("code").GetString());
        Assert.False(tokenProvider.GenerateInternalTokenCalled);
    }

    [Fact]
    public async Task PublicEndpoint_SwapsAuthorizationForServiceToServiceToken()
    {
        var s2sToken = InternalTokenTransformTestHarness.WriteTokenWithJti(S2STokenJti);
        var tokenProvider = new StubInternalTokenProvider { S2SToken = s2sToken };
        using var services = BuildServices(tokenProvider);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Constants.PublicAccessClaimType, "true")],
            "TestAuthType"));
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services);

        var requestContext = await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Equal($"Bearer {s2sToken}", requestContext.ProxyRequest.Headers.Authorization!.ToString());
        Assert.False(tokenProvider.GenerateInternalTokenCalled);
        Assert.Equal(S2STokenJti, httpContext.Items[InternalTokenTransformExtensions.ChainTokenIdItemKey]);
    }

    [Fact]
    public async Task PublicEndpoint_SetsDiagnosticContextToTheS2STokensOwnJti()
    {
        var diagnosticContext = new Mock<IDiagnosticContext>();
        var s2sToken = InternalTokenTransformTestHarness.WriteTokenWithJti(S2STokenJti);
        var tokenProvider = new StubInternalTokenProvider { S2SToken = s2sToken };
        using var services = BuildServices(tokenProvider, diagnosticContext.Object);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Constants.PublicAccessClaimType, "true")],
            "TestAuthType"));
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services);

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        diagnosticContext.Verify(d => d.Set("auth.jti", S2STokenJti, false), Times.Once);
    }

    [Fact]
    public async Task Should_LeaveTheAuthorizationUntouchedAndLogAnError_When_TheS2STokenCannotBeMinted()
    {
        using var logs = new CapturingLoggerProvider();
        var tokenProvider = new StubInternalTokenProvider { S2SToken = null };
        using var services = BuildServices(tokenProvider, logs, endpointPermissionService: null);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Constants.PublicAccessClaimType, "true")], "TestAuthType"));
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services);
        httpContext.Request.Path = "/api/public";

        var requestContext = await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Null(requestContext.ProxyRequest.Headers.Authorization);
        Assert.False(httpContext.Items.ContainsKey(InternalTokenTransformExtensions.ChainTokenIdItemKey));
        var failure = Assert.Single(logs.All, entry => entry.EventId.Id == 11);
        Assert.Equal(LogLevel.Error, failure.Level);
        Assert.Equal("Failed to generate S2S token for public endpoint /api/public", failure.Message);
    }

    [Fact]
    public async Task Should_ReplaceTheAuthorizationWithTheS2STokenWithoutPublishingAChainTokenId_When_APermissionCheckedRouteIsPublic()
    {
        using var logs = new CapturingLoggerProvider();
        var s2sToken = InternalTokenTransformTestHarness.WriteTokenWithJti(S2STokenJti);
        var tokenProvider = new StubInternalTokenProvider { S2SToken = s2sToken };
        using var services = BuildServices(tokenProvider, logs, endpointPermissionService: null);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildPublicUser(), services, OriginalAuthorizationHeader);
        httpContext.Request.Path = "/api/public";

        var requestContext = await InternalTokenTransformTestHarness.ApplyPermissionCheckTransformAsync(httpContext, OriginalAuthorizationHeader);

        Assert.Equal($"Bearer {s2sToken}", requestContext.ProxyRequest.Headers.Authorization!.ToString());
        Assert.False(httpContext.Items.ContainsKey(InternalTokenTransformExtensions.ChainTokenIdItemKey));
        Assert.False(tokenProvider.GenerateInternalTokenCalled);
        Assert.Equal(
            [
                "Public endpoint detected, generating S2S token for /api/public",
                "S2S token added for public endpoint /api/public",
            ],
            logs.All.Select(entry => entry.Message));
    }

    [Fact]
    public async Task Should_LeaveTheAuthorizationUntouchedAndLogAnError_When_APermissionCheckedPublicRouteCannotMintTheS2SToken()
    {
        using var logs = new CapturingLoggerProvider();
        var tokenProvider = new StubInternalTokenProvider { S2SToken = null };
        using var services = BuildServices(tokenProvider, logs, endpointPermissionService: null);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildPublicUser(), services, OriginalAuthorizationHeader);
        httpContext.Request.Path = "/api/public";

        var requestContext = await InternalTokenTransformTestHarness.ApplyPermissionCheckTransformAsync(httpContext, OriginalAuthorizationHeader);

        Assert.Equal(OriginalAuthorizationHeader, requestContext.ProxyRequest.Headers.Authorization!.ToString());
        Assert.False(httpContext.Items.ContainsKey(InternalTokenTransformExtensions.ChainTokenIdItemKey));
        Assert.False(httpContext.Response.HasStarted);
        Assert.Equal(
            [
                (LogLevel.Debug, "Public endpoint detected, generating S2S token for /api/public"),
                (LogLevel.Error, "Failed to generate S2S token for public endpoint /api/public"),
            ],
            logs.All.Select(entry => (entry.Level, entry.Message)));
    }

    [Fact]
    public async Task Should_Answer401WithoutMinting_When_TheUserIsNotAuthenticated()
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = "unused" };
        using var services = BuildServices(tokenProvider);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "sub-1")]));
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(anonymous, services, OriginalAuthorizationHeader);
        using var body = new System.IO.MemoryStream();
        httpContext.Response.Body = body;

        var requestContext = await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Equal("auth.unauthorized", ProblemCode(body));
        Assert.False(tokenProvider.GenerateInternalTokenCalled);
        Assert.False(requestContext.ProxyRequest.Headers.Contains("X-Original-Authorization"));
    }

    [Fact]
    public async Task Should_LogAWarning_When_AnAuthenticatedUserSendsNoAuthorizationHeader()
    {
        using var logs = new CapturingLoggerProvider();
        var tokenProvider = new StubInternalTokenProvider { InternalToken = "unused" };
        using var services = BuildServices(tokenProvider, logs, endpointPermissionService: null);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildBearerUser(), services, incomingAuthorization: null);
        httpContext.Request.Path = "/api/things";
        using var body = new System.IO.MemoryStream();
        httpContext.Response.Body = body;

        await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        var warning = Assert.Single(logs.All, entry => entry.EventId.Id == 14);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("No Authorization header for authenticated user on path /api/things", warning.Message);
    }

    [Fact]
    public async Task Should_Answer403WithoutMinting_When_TheEndpointHasNoPermissionMapping()
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = InternalTokenTransformTestHarness.WriteTokenWithJti(MintedInternalTokenJti) };
        var endpointPermissionService = new Mock<IEndpointPermissionService>();
        endpointPermissionService
            .Setup(s => s.GetEndpointPermissionsByPathAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EndpointPermissionMatch?, Error>.Success(null));
        using var services = BuildServices(tokenProvider, logs: null, endpointPermissionService.Object);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildBearerUser(), services, OriginalAuthorizationHeader);
        httpContext.Request.Path = "/api/unmapped";
        httpContext.Request.Method = HttpMethods.Get;
        using var body = new System.IO.MemoryStream();
        httpContext.Response.Body = body;

        var requestContext = await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
        Assert.Equal("auth.no_permission_mapping", ProblemCode(body));
        Assert.False(tokenProvider.GenerateInternalTokenCalled);
        Assert.Null(requestContext.ProxyRequest.Headers.Authorization);
        Assert.Equal(OriginalAuthorizationHeader, requestContext.ProxyRequest.Headers.GetValues("X-Original-Authorization").Single());
    }

    [Fact]
    public async Task Should_Answer401AndLogAnError_When_TheInternalTokenCannotBeMinted()
    {
        using var logs = new CapturingLoggerProvider();
        var tokenProvider = new StubInternalTokenProvider { InternalToken = null };
        using var services = BuildServices(tokenProvider, logs, endpointPermissionService: null);
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(BuildBearerUser(), services, OriginalAuthorizationHeader);
        using var body = new System.IO.MemoryStream();
        httpContext.Response.Body = body;

        var requestContext = await InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext);

        Assert.True(tokenProvider.GenerateInternalTokenCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, httpContext.Response.StatusCode);
        Assert.Equal("auth.token_generation_failed", ProblemCode(body));
        Assert.Null(requestContext.ProxyRequest.Headers.Authorization);
        Assert.False(httpContext.Items.ContainsKey(InternalTokenTransformExtensions.ChainTokenIdItemKey));
        var error = Assert.Single(logs.All, entry => entry.Level == LogLevel.Error);
        Assert.Equal("Failed to generate internal token for subject sub-1 — rejecting request", error.Message);
    }

    [Fact]
    public async Task Should_ThrowForTheUnknownMethod_When_ThePrincipalCarriesNoAuthorizationMethod()
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = "unused" };
        using var services = BuildServices(tokenProvider);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, "sub-1"), new Claim(JwtRegisteredClaimNames.PreferredUsername, "user-1")],
            "TestAuthType"));
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services, OriginalAuthorizationHeader);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext));

        Assert.Equal("Unknown authorization method:  (Parameter 'method')", exception.Message);
        Assert.False(tokenProvider.GenerateInternalTokenCalled);
    }

    [Theory]
    [InlineData(JwtRegisteredClaimNames.Sub, "subjectClaim")]
    [InlineData(JwtRegisteredClaimNames.PreferredUsername, "usernameClaim")]
    public async Task Should_ThrowWithoutMinting_When_AnIdentityClaimIsMissing(string missingClaim, string parameterName)
    {
        var tokenProvider = new StubInternalTokenProvider { InternalToken = "unused" };
        using var services = BuildServices(tokenProvider);
        var claims = BuildApiKeyUser().Claims.Where(claim => claim.Type != missingClaim);
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
        var httpContext = InternalTokenTransformTestHarness.BuildHttpContext(user, services, "ApiKey some-key");

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => InternalTokenTransformTestHarness.ApplyInternalTokenTransformAsync(httpContext));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.False(tokenProvider.GenerateInternalTokenCalled);
    }

    private static string? ProblemCode(System.IO.MemoryStream body)
    {
        using var problem = System.Text.Json.JsonDocument.Parse(body.ToArray());
        return problem.RootElement.GetProperty("code").GetString();
    }

    private sealed class StubPermissionsCache : IUserPermissionsCache
    {
        public Task<UserPermissionsSnapshot> GetAsync(string idpUserId, CancellationToken ct)
            => Task.FromResult(new UserPermissionsSnapshot { EffectiveRoleClaim = StubEffectiveRole });
        public Task InvalidateAsync(string idpUserId, CancellationToken ct)
            => Task.CompletedTask;

        public Task InvalidateAllAsync(CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class StubInternalTokenProvider : IInternalTokenProvider
    {
        public string? InternalToken { get; init; }
        public string? S2SToken { get; init; }
        public bool GenerateInternalTokenCalled { get; private set; }

        public EAuthorizationMethod LastAuthMethod { get; private set; }
        public string? LastSubject { get; private set; }
        public string? LastUsername { get; private set; }
        public string? LastProviderId { get; private set; }
        public string? LastSystemRole { get; private set; }
        public string? LastOriginalJti { get; private set; }

        public Task<string?> GenerateInternalTokenAsync(EAuthorizationMethod authMethod, string subject, string username, string systemRole, string? providerId, string? email, IDictionary<string, ICollection<string>>? projectRoles, string? providerName, string? originalJti)
        {
            GenerateInternalTokenCalled = true;
            LastAuthMethod = authMethod;
            LastSubject = subject;
            LastUsername = username;
            LastProviderId = providerId;
            LastSystemRole = systemRole;
            LastOriginalJti = originalJti;
            return Task.FromResult(InternalToken);
        }

        public Task<string?> GenerateServiceToServiceTokenAsync(string originService, string targetAudience = "forge")
            => Task.FromResult(S2SToken);

        public Task<ClaimsPrincipal?> ValidateInternalTokenAsync(string token)
            => throw new NotSupportedException();

        public string GetCurrentKeyId()
            => throw new NotSupportedException();
    }
}
