using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Serilog;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Startup
{
    internal static class InternalTokenTransformExtensions
    {
        /// <summary>
        /// <see cref="HttpContext.Items"/> key under which the token id that identifies this request's
        /// caller (<c>original_jti ?? jti</c> of the forwarded token) is published for logging.
        /// </summary>
        internal const string ChainTokenIdItemKey = "auth.jti";

        // JwtSecurityTokenHandler is thread-safe for reading; share a single instance.
        private static readonly JwtSecurityTokenHandler _tokenReader = new();

        /// <summary>
        /// Adds a request transform that verifies the authenticated user has the required permissions
        /// for the requested endpoint according to <see cref="Forge.Core.Features.EndpointMap.Services.IEndpointPermissionService"/>.
        /// Returns 401 for unauthenticated requests and 403 for insufficient permissions.
        /// Intended for forwarders that manage their own downstream auth (e.g. Cyclops) and do not use <see cref="AddInternalTokenTransform"/>.
        /// </summary>
        public static void AddPermissionCheckTransform(this TransformBuilderContext transformBuilder)
        {
            transformBuilder.AddRequestTransform(async context =>
            {
                var httpCtx = context.HttpContext;
                var svc = httpCtx.RequestServices;
                var user = httpCtx.User;
                var logger = svc.GetRequiredService<ILogger<Program>>();
                var tokenService = svc.GetRequiredService<IInternalTokenProvider>();
                var permissionsCache = svc.GetRequiredService<IUserPermissionsCache>();

                // CASE 1: Public endpoint (authenticated with PublicWithInternalTokenHandler)
                if (user?.HasClaim(Constants.PublicAccessClaimType, "true") == true)
                {
                    if (await AddServiceTokenAsync(context, tokenService, logger) is not null)
                    {
                        logger.LogS2STokenAdded(context.HttpContext.Request.Path.Value);
                    }

                    return;
                }

                if (user?.Identity?.IsAuthenticated is null or false)
                {
                    await ProblemResponse.WriteAuthenticationFailureAsync(httpCtx);
                    return;
                }

                var endpointPermissionService = svc.GetService<Forge.Core.Features.EndpointMap.Services.IEndpointPermissionService>();

                await AuthorizationHelpers.AuthorizeRequestAsync(
                    httpCtx,
                    endpointPermissionService,
                    user,
                    permissionsCache,
                    svc);
                // 401/403 already written by AuthorizeRequestAsync on failure;
                // YARP will short-circuit forwarding when Response.HasStarted is true.
            });
        }

        public static void AddInternalTokenTransform(this TransformBuilderContext transformBuilder)
            => transformBuilder.AddRequestTransform(ForwardWithInternalTokenAsync);

        private static async ValueTask ForwardWithInternalTokenAsync(RequestTransformContext context)
        {
            var user = context.HttpContext.User;
            var svc = context.HttpContext.RequestServices;
            var tokenService = svc.GetRequiredService<IInternalTokenProvider>();
            var logger = svc.GetRequiredService<ILogger<Program>>();

            // CASE 1: Public endpoint (authenticated with PublicWithInternalTokenHandler)
            if (user?.HasClaim(Constants.PublicAccessClaimType, "true") == true)
            {
                await ForwardPublicRequestAsync(context, tokenService, logger);
                return;
            }

            // CASE 2: Authenticated user with Bearer/ApiKey/Cookie
            if (user?.Identity?.IsAuthenticated is null or false)
            {
                await ProblemResponse.WriteAuthenticationFailureAsync(context.HttpContext);
                return;
            }

            var endpointPermissionService = svc.GetService<Forge.Core.Features.EndpointMap.Services.IEndpointPermissionService>();
            var permissionsCache = svc.GetRequiredService<IUserPermissionsCache>();

            if (tokenService is null)
            {
                logger.LogTokenServiceMissing(context.HttpContext.Request.Path.Value);
                await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status500InternalServerError,
                    "auth.token_service_unavailable", "Token service unavailable",
                    "Internal token service is not available.");
                return;
            }

            var incomingAuth = context.HttpContext.Request.Headers["Authorization"].FirstOrDefault();

            if (incomingAuth is null)
            {
                logger.LogNoAuthorizationHeader(context.HttpContext.Request.Path.Value);
                await ProblemResponse.WriteAuthenticationFailureAsync(context.HttpContext);
                return;
            }

            context.ProxyRequest.Headers.Remove("X-Original-Authorization");
            context.ProxyRequest.Headers.TryAddWithoutValidation("X-Original-Authorization", incomingAuth);

            var isAuthorized = await AuthorizationHelpers.AuthorizeRequestAsync(
                context.HttpContext,
                endpointPermissionService,
                user,
                permissionsCache,
                svc);

            if (!isAuthorized)
            {
                return; // 403 response already written
            }

            await ForwardInternalTokenAsync(context, user, tokenService, permissionsCache, logger);
        }

        /// <summary>
        /// Forwards a public-endpoint request with a gateway service-to-service token and publishes that
        /// token's id for logging; leaves the request untouched when no token can be minted.
        /// </summary>
        private static async Task ForwardPublicRequestAsync(RequestTransformContext context, IInternalTokenProvider tokenService, ILogger<Program> logger)
        {
            var s2sToken = await AddServiceTokenAsync(context, tokenService, logger);
            if (s2sToken is null)
            {
                return;
            }

            PublishChainTokenId(context.HttpContext, originalJti: null, forwardedToken: s2sToken);
            logger.LogS2STokenAdded(context.HttpContext.Request.Path.Value);
        }

        /// <summary>
        /// Replaces the Authorization header of a public-endpoint request with a gateway service-to-service
        /// token and returns it; returns null and leaves the request untouched when no token can be minted.
        /// </summary>
        private static async Task<string?> AddServiceTokenAsync(RequestTransformContext context, IInternalTokenProvider tokenService, ILogger<Program> logger)
        {
            logger.LogPublicEndpointDetected(context.HttpContext.Request.Path.Value);

            var s2sToken = await tokenService.GenerateServiceToServiceTokenAsync("gateway", Environment.GetEnvironmentVariable("Idp__Audience"));

            if (string.IsNullOrEmpty(s2sToken))
            {
                logger.LogS2STokenGenerationFailed(context.HttpContext.Request.Path.Value);
                return null;
            }

            context.ProxyRequest.Headers.Remove("Authorization");
            context.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {s2sToken}");
            return s2sToken;
        }

        /// <summary>
        /// Mints the internal token for an authorized caller and puts it into the Authorization header;
        /// answers 401 when the token cannot be minted.
        /// </summary>
        private static async Task ForwardInternalTokenAsync(
            RequestTransformContext context,
            ClaimsPrincipal user,
            IInternalTokenProvider tokenService,
            IUserPermissionsCache permissionsCache,
            ILogger<Program> logger)
        {
            var svc = context.HttpContext.RequestServices;
            var subjectClaim = user.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var usernameClaim = user.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername);
            var emailClaim = user.FindFirstValue(JwtRegisteredClaimNames.Email);
            var providerClaim = user.FindFirstValue(AuthConstants.ProviderIdClaimType);
            var authMethod = EAuthorizationMethod.FromString(user.FindFirstValue(EAuthorizationMethod.ClaimType) ?? string.Empty);

            var incomingJti = ReadIncomingTokenId(user);
            string? roleClaim = user.FindFirstValue(ClaimTypes.Role);
            ArgumentNullException.ThrowIfNull(subjectClaim);
            ArgumentNullException.ThrowIfNull(usernameClaim);

            string? providerName = null;
            IDictionary<string, ICollection<string>>? projectRecords = new Dictionary<string, ICollection<string>>();
            if (authMethod is EAuthorizationMethod.JwtBearerToken)
            {
                // Use cached snapshot — permissions are already fetched by AuthorizeRequestAsync above
                var snapshot = await permissionsCache.GetAsync(subjectClaim, context.HttpContext.RequestAborted);
                projectRecords = snapshot.ProjectRecords;
                roleClaim = snapshot.EffectiveRoleClaim;
                providerName = await ResolveAdministeredProviderNameAsync(svc, snapshot.EffectiveRoleClaim, subjectClaim, context.HttpContext.RequestAborted);
            }

            // Additive providerName claim (ADR-0011): resolve the provider service-key's id (Guid)
            // to the provider's natural name so Core can scope provider/location writes by claim.
            // Gated on the authorization method so the interactive mint above cannot be overwritten by a
            // self-asserted provider_id — the claim ingress already discards, kept unusable here too.
            if (authMethod is EAuthorizationMethod.ApiKey)
            {
                providerName = await ResolveServiceKeyProviderNameAsync(svc, providerClaim, context.HttpContext.RequestAborted);
            }

            var internalToken = await tokenService.GenerateInternalTokenAsync(authMethod, subjectClaim, usernameClaim, roleClaim, providerClaim, emailClaim, projectRecords, providerName, incomingJti);

            // If token generation failed, fail the request (do not forward an invalid Authorization header)
            if (string.IsNullOrEmpty(internalToken))
            {
                logger.LogError("Failed to generate internal token for subject {Subject} — rejecting request", subjectClaim);
                await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                    "auth.token_generation_failed", "Token generation failed",
                    "Failed to generate internal authentication token. Please try again.");
                return;
            }

            context.ProxyRequest.Headers.Remove("Authorization");
            context.ProxyRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {internalToken}");
            PublishChainTokenId(context.HttpContext, originalJti: incomingJti, forwardedToken: internalToken);
        }

        /// <summary>
        /// The provider name of a provider service key: its <c>provider_id</c> claim must be a Guid the
        /// resolver knows; null when the claim is missing or not a Guid, or no resolver is registered.
        /// </summary>
        private static async Task<string?> ResolveServiceKeyProviderNameAsync(
            IServiceProvider services,
            string? providerClaim,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(providerClaim) || !Guid.TryParse(providerClaim, out var providerGuid))
                return null;

            var providerResolver = services.GetService<Forge.Core.Features.UserOrgRoles.IProviderIdentityResolver>();
            if (providerResolver is null)
                return null;

            return await providerResolver.GetProviderNameAsync(providerGuid, cancellationToken);
        }

        /// <summary>
        /// The provider an interactive ProviderAdmin administers, or null for every other interactive caller
        /// (D5, extending ADR-0011 to human callers). Both halves of the predicate must hold — the effective
        /// role from the permissions snapshot <b>and</b> a provider still assigned to the user — so the stale
        /// <c>User.AssignedProviderId</c> a demotion can leave behind buys no provider-tier reads. Called only
        /// from the JwtBearerToken branch: a forged <c>am</c> must never reach it.
        /// </summary>
        private static async Task<string?> ResolveAdministeredProviderNameAsync(
            IServiceProvider services,
            string effectiveRoleClaim,
            string idpUserId,
            CancellationToken cancellationToken)
        {
            if (effectiveRoleClaim != RoleInfoConstants.ProviderAdministratorRoleName)
                return null;

            var providerResolver = services.GetService<Forge.Core.Features.UserOrgRoles.IProviderIdentityResolver>();
            if (providerResolver is null)
                return null;

            return await providerResolver.GetAssignedProviderNameAsync(idpUserId, cancellationToken);
        }

        /// <summary>
        /// Reads the token id the caller arrived with. Only the JwtBearer scheme yields one — ApiKey and
        /// Cookie principals are built from scratch — and an empty jti counts as no jti, the single
        /// definition every downstream <c>original_jti ?? jti</c> fallback relies on.
        /// </summary>
        private static string? ReadIncomingTokenId(ClaimsPrincipal user)
            => user.FindFirstValue(JwtRegisteredClaimNames.Jti) is { Length: > 0 } incomingJti ? incomingJti : null;

        private static void PublishChainTokenId(HttpContext httpContext, string? originalJti, string forwardedToken)
        {
            var chainTokenId = originalJti ?? _tokenReader.ReadJwtToken(forwardedToken).Id;
            httpContext.Items[ChainTokenIdItemKey] = chainTokenId;

            // The Bearer OnTokenValidated handler already set this from the incoming principal; for
            // ApiKey/Cookie/public/S2S requests (no incoming token) this is the only place the value —
            // the internal token's own jti — is known (ADR 0093, criterion 3). A missing logging
            // side-channel must never break request forwarding.
            httpContext.RequestServices.GetService<IDiagnosticContext>()?.Set(AuthTelemetry.TagJti, chainTokenId);
        }
    }
}