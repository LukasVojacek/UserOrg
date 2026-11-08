using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Serilog;

using UserOrg.Gateway.Authentication;
using UserOrg.Gateway.Configuration;
using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Shared;
using UserOrg.Shared.Logging;

namespace UserOrg.Gateway.Startup;

internal static class ServicesExtensions
{
    internal const string BearerTokenItemKey = "BearerToken";

    /// <summary>
    /// The claims a caller could assert about its own identity, which the Gateway resolves itself and no
    /// incoming token may influence: how the caller authenticated, and which provider it acts for.
    /// </summary>
    private static readonly string[] SelfAssertedIdentityClaimTypes =
        [EAuthorizationMethod.ClaimType, AuthConstants.ProviderIdClaimType];

    private static readonly AuthenticationFailure UnclassifiedFailure = new(
        StatusCodes.Status401Unauthorized, "auth.authentication_failed",
        "Authentication failed", "Request was not authenticated.");

    // First match wins: a specific exception type has to precede the base type it derives from.
    private static readonly (Type ExceptionType, AuthenticationFailure Failure)[] FailureByExceptionType =
    [
        (typeof(HttpRequestException), new(StatusCodes.Status502BadGateway, "auth.external_service_error",
            "External authentication service error", "OIDC provider unreachable. Please try again later.")),
        (typeof(OperationCanceledException), new(StatusCodes.Status503ServiceUnavailable, "auth.service_timeout",
            "Authentication service timeout", "Token validation timeout. Please try again.")),
        (typeof(SecurityTokenSignatureKeyNotFoundException), new(StatusCodes.Status422UnprocessableEntity, "auth.token_signature_unavailable",
            "Token signature validation unavailable", "Cannot validate token signature. Check token format.")),
        (typeof(SecurityTokenInvalidSignatureException), new(StatusCodes.Status422UnprocessableEntity, "auth.token_invalid_signature",
            "Invalid token signature", "Token signature validation failed.")),
        (typeof(SecurityTokenExpiredException), new(StatusCodes.Status401Unauthorized, "auth.token_expired",
            "Token expired", "Access token has expired. Please refresh.")),
        (typeof(SecurityTokenMalformedException), new(StatusCodes.Status422UnprocessableEntity, "auth.token_malformed",
            "Malformed token", "Token format is invalid.")),
        (typeof(SecurityTokenInvalidAudienceException), new(StatusCodes.Status422UnprocessableEntity, "auth.token_invalid_audience",
            "Invalid token audience", "Token audience does not match expected value.")),
        (typeof(SecurityTokenInvalidIssuerException), new(StatusCodes.Status422UnprocessableEntity, "auth.token_invalid_issuer",
            "Invalid token issuer", "Token issuer is not recognized.")),
        (typeof(SecurityTokenNoExpirationException), new(StatusCodes.Status422UnprocessableEntity, "auth.token_no_expiration",
            "Token missing expiration", "Token does not have required expiration claim.")),
        (typeof(SecurityTokenException), new(StatusCodes.Status422UnprocessableEntity, "auth.token_validation_failed",
            "Token validation failed", "The bearer token could not be validated.")),
    ];

    internal static void AddAuthenticationServices(this WebApplicationBuilder builder)
    {
        AddRoleManagementPathPrefixDefault(builder);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(builder.Configuration);

        // Ensure inbound JWT claim names are preserved (do not map "sub" -> ClaimTypes.NameIdentifier etc.)
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

        AddJwtKeyCaching(builder.Services);
        builder.Services.AddInternalTokenFeature(builder.Configuration);
        AddGatewayAuthOptions(builder);

        var idp = IdpSettings.Read(builder.Configuration);

        // Note: Gateway internal token validation is handled by UserOrg API configuration.

        // Develop-only override is gated first by the environment (ADR-0045 gate 1): outside Development the
        // scheme is never registered and the selector never forwards to it.
        AddAuthenticationSchemes(builder, idp, builder.Environment.IsDevelopment());
    }

    /// <summary>Defaults <see cref="RoleManagementOptions.PathPrefix"/> to the gateway PathBase when not explicitly set.</summary>
    private static void AddRoleManagementPathPrefixDefault(WebApplicationBuilder builder)
    {
        builder.Services.PostConfigure<RoleManagementOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.PathPrefix))
            {
                var pb = builder.Configuration["GATEWAY__PATHBASE"] ?? builder.Configuration["Gateway:PathBase"];
                if (!string.IsNullOrWhiteSpace(pb))
                {
                    opts.PathPrefix = NormalizePathBase(pb);
                }
            }
        });
    }

    private static string NormalizePathBase(string pathBase)
        => pathBase.StartsWith('/') ? pathBase.TrimEnd('/') : $"/{pathBase.TrimEnd('/')}";

    private static void AddJwtKeyCaching(IServiceCollection services)
    {
        services.AddDistributedMemoryCache();
        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                LocalCacheExpiration = TimeSpan.FromMinutes(30)
            };
        });
    }

    private static void AddGatewayAuthOptions(WebApplicationBuilder builder)
    {
        var isV1ApiEnabled = builder.Configuration.GetValue($"FeatureManagement:{UserOrg.Shared.FeatureManagement.FeatureFlags.V1Api}", true);
        builder.Services.AddOptions<PublicEndpointsOptions>()
            .Configure(o => o.Paths = PublicEndpointsOptions.GetDefaultPaths(isV1ApiEnabled))
            .BindConfiguration(PublicEndpointsOptions.SectionName)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PublicEndpointsOptions>, PublicEndpointsOptionsValidator>();

        // Develop-only auth override (ADR-0045). Bound everywhere, but only ever consulted when the
        // environment is Development (the scheme is registered conditionally in AddAuthenticationSchemes).
        builder.Services.AddOptions<DevAuthOverrideOptions>()
            .BindConfiguration(DevAuthOverrideOptions.SectionName);

        builder.Services.AddOptions<ApiKeyAuthenticationOptions>()
            .BindConfiguration(ApiKeyAuthenticationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddHttpClient("IntrospectionBackChannel", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        builder.Services.AddSingleton<ITokenIntrospectionService, TokenIntrospectionService>();
        builder.Services.AddHostedService<IntrospectionStartupProbe>();
    }

    /// <summary>A setting of the <c>Idp</c> section, falling back to its <c>Idp__</c> environment variable.</summary>
    private static string? ReadIdpSetting(IConfiguration configuration, string name)
        => configuration[$"Idp:{name}"] ?? Environment.GetEnvironmentVariable($"Idp__{name}");

    /// <summary>The comma-separated audiences, trimmed, without empty entries and ordinal duplicates; null when unset.</summary>
    private static string[]? ParseAdditionalAudiences(string? value)
        => value?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static audience => !string.IsNullOrWhiteSpace(audience))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The IdP settings the Bearer scheme validates tokens against, with the strict audience check of ADR 0121
    /// (<c>FeatureManagement:StrictJwtAudience</c>).
    /// </summary>
    private readonly record struct IdpSettings(string Authority, string Audience, string[]? AdditionalAudiences, bool StrictAudience)
    {
        /// <summary>
        /// Reads the settings; throws when the authority or the audience is missing or empty, and an
        /// <see cref="InvalidOperationException"/> when the strict audience flag is neither blank nor a boolean.
        /// </summary>
        public static IdpSettings Read(IConfiguration configuration)
        {
            var authAuthority = ReadIdpSetting(configuration, "Authority");
            var audience = ReadIdpSetting(configuration, "Audience");
            var additionalAudiences = ParseAdditionalAudiences(ReadIdpSetting(configuration, "AdditionalAudiences"));

            ArgumentNullException.ThrowIfNullOrEmpty(authAuthority);
            ArgumentNullException.ThrowIfNullOrEmpty(audience);

            var strictAudience = GatewayFeatureFlags.IsEnabledByDefault(configuration, UserOrg.Shared.FeatureManagement.FeatureFlags.StrictJwtAudience);

            return new IdpSettings(authAuthority, audience, additionalAudiences, strictAudience);
        }
    }

    private static void AddAuthenticationSchemes(WebApplicationBuilder builder, IdpSettings idp, bool isDevelopment)
    {
        // Configure authentication: use a policy scheme as the default which forwards to appropriate schemes
        var authenticationBuilder = builder.Services
            .AddAuthentication(Constants.DynamicSchemeId)
            .AddPolicyScheme(Constants.DynamicSchemeId, "Dynamic authentication selector", options =>
                options.ForwardDefaultSelector = context => AuthenticationSchemeSelector.Select(context, isDevelopment))
            .AddJwtBearer(Constants.BearerSchemeId, options => ConfigureJwtBearerOptions(options, idp, builder.Environment))
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Constants.ApiKeySchemeId, _ => { })
            .AddCookie(Constants.RoleManagementCookieSchemeId, options => ConfigureCookieOptions(options, builder.Services))
            .AddScheme<AuthenticationSchemeOptions, PublicWithInternalTokenHandler>(Constants.PublicWithInternalTokenSchemeId, _ => { });

        builder.Services.AddCascadingAuthenticationState();

        // ADR-0045 gate 1: the Develop-only override scheme exists ONLY in Development. In any other
        // environment it is never registered, so the selector cannot forward to it whatever the config says.
        if (isDevelopment)
        {
            authenticationBuilder
                .AddScheme<AuthenticationSchemeOptions, DevOverrideAuthenticationHandler>(Constants.DevOverrideSchemeId, _ => { });
        }
    }

    private static void ConfigureCookieOptions(CookieAuthenticationOptions options, IServiceCollection services)
    {
        using var serviceProvider = services.BuildServiceProvider();
        var rmOptions = serviceProvider.GetService<RoleManagementOptions>();

        options.Cookie.Name = rmOptions?.CookieName ?? "RoleManagementAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(rmOptions?.CookieExpirationMinutes ?? 60);
        options.SlidingExpiration = true;

        // Don't redirect to login page - we handle authentication in middleware
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                // Return 401 instead of redirecting
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                // Return 403 instead of redirecting
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    }

    private static void ConfigureJwtBearerOptions(
        JwtBearerOptions options,
        IdpSettings idp,
        IWebHostEnvironment environment)
    {
        options.SaveToken = true;
        options.Authority = idp.Authority;
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = !environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // Trimmed: the authority may be configured with a trailing slash (the AppHost's default
            // carries one so the idp health check resolves against the realm), while the iss claim
            // never has one. Comparing them verbatim would reject every token the moment discovery
            // stops supplying the issuer for us.
            ValidIssuer = idp.Authority?.TrimEnd('/'),
            ValidAudience = idp.Audience,
            ValidAudiences = idp.AdditionalAudiences is null || idp.AdditionalAudiences.Length == 0
                ? [idp.Audience]
                : [idp.Audience, .. idp.AdditionalAudiences],
            AudienceValidator = StrictAudienceValidation.For(idp.StrictAudience),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            ClockSkew = TimeSpan.FromSeconds(50) //TODO: should be in configuration
        };

        options.Events = CreateJwtBearerEvents(idp.Authority);
    }

    internal static JwtBearerEvents CreateJwtBearerEvents(string authAuthority)
    {
        return new JwtBearerEvents
        {
            OnMessageReceived = OnBearerMessageReceived,

            OnTokenValidated = OnBearerTokenValidatedAsync,

            OnAuthenticationFailed = context => OnBearerAuthenticationFailed(context, authAuthority),

            OnChallenge = async context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                logger.LogChallenge(context.Request.Path.Value, context.Error, context.ErrorDescription);

                context.HandleResponse();
                await ProblemResponse.WriteAuthenticationFailureAsync(context.HttpContext);
            },

            OnForbidden = async context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogForbidden(context.Request.Path.Value);
                await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, "auth.forbidden", "Access forbidden", "Insufficient permissions.");
            }
        };
    }

    private static Task OnBearerMessageReceived(MessageReceivedContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        var authHeader = context.Request.Headers.Authorization.ToString();

        // Store raw token for introspection in OnTokenValidated
        if (!string.IsNullOrEmpty(authHeader))
        {
            context.HttpContext.Items[BearerTokenItemKey] = authHeader;
        }

        using var activity = AuthTelemetry.ActivitySource.StartActivity(AuthTelemetry.ActivityMessageReceived, System.Diagnostics.ActivityKind.Server);
        activity?.SetTag(AuthTelemetry.TagAuthority, context.Options.Authority);
        activity?.SetTag(AuthTelemetry.TagScheme, context.Scheme.Name);
        activity?.SetTag(AuthTelemetry.TagHasAuthorization, !string.IsNullOrEmpty(authHeader));

        logger.LogMessageReceived(context.Request.Path.Value, authHeader.Length > 0);

        return Task.CompletedTask;
    }

    private static Task OnBearerAuthenticationFailed(AuthenticationFailedContext context, string authAuthority)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        var exception = context.Exception;

        using var activity = AuthTelemetry.ActivitySource.StartActivity(AuthTelemetry.ActivityAuthenticationFailed, System.Diagnostics.ActivityKind.Server);
        activity?.SetTag(AuthTelemetry.TagAuthority, authAuthority);
        activity?.SetTag(AuthTelemetry.TagException, exception.GetType().Name);

        var failure = ClassifyAuthenticationFailure(exception);
        logger.LogAuthenticationFailed(AuthenticationFailureLogLevel(exception), exception, context.Request.Path.Value, failure.Code, exception.GetType().Name, failure.Status, authAuthority);
        LogAuthenticationFailureDetails(logger, exception);

        // Only classify here: the handler keeps running the pipeline after this event, so writing the
        // body now would break anonymous routes. OnChallenge / the forwarder transform render it.
        context.HttpContext.Items[ProblemResponse.AuthenticationFailureItemKey] = failure;

        return Task.CompletedTask;
    }

    private static AuthenticationFailure ClassifyAuthenticationFailure(Exception exception)
        => FailureByExceptionType
            .Where(entry => entry.ExceptionType.IsInstanceOfType(exception))
            .Select(entry => entry.Failure)
            .FirstOrDefault() ?? UnclassifiedFailure;

    private static LogLevel AuthenticationFailureLogLevel(Exception exception) => exception switch
    {
        SecurityTokenExpiredException => LogLevel.Information,
        SecurityTokenSignatureKeyNotFoundException => LogLevel.Error,
        SecurityTokenException => LogLevel.Warning,
        _ => LogLevel.Error
    };

    private static void LogAuthenticationFailureDetails(ILogger<Program> logger, Exception exception)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        logger.LogAuthenticationFailureDetail(exception);

        if (exception.InnerException != null)
        {
            logger.LogAuthenticationInnerException(exception.InnerException.GetType().Name, exception.InnerException.Message);
        }
    }

    /// <summary>One bearer token being validated: its context, principal and the telemetry sinks of the validation.</summary>
    private readonly record struct ValidatedToken(
        TokenValidatedContext Context,
        ClaimsPrincipal Principal,
        System.Diagnostics.Activity? Activity,
        ILogger<Program> Logger)
    {
        public string UserId => Principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "unknown";
    }

    private static async Task OnBearerTokenValidatedAsync(TokenValidatedContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

        using var activity = AuthTelemetry.ActivitySource.StartActivity(AuthTelemetry.ActivityTokenValidated, System.Diagnostics.ActivityKind.Server);
        if (context.Principal is not null)
        {
            await ValidatePrincipalAsync(new ValidatedToken(context, context.Principal, activity, logger));
        }
    }

    private static async Task ValidatePrincipalAsync(ValidatedToken validated)
    {
        validated.Activity?.SetTag(AuthTelemetry.TagUserId, validated.UserId);

        RecordTokenId(validated);
        LogValidatedClaims(validated);

        // preserve existing behavior: a token without sub is rejected; preferred_username falls back to sub
        var extractedSub = validated.Principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(extractedSub))
        {
            FailMissingSub(validated, extractedSub);
            return;
        }

        // FIP don´t have PreferredUserName
        var preferredUsername = validated.Principal.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername) ?? extractedSub;

        // Only a token that passed local JWT validation reaches the introspection endpoint
        if (await PassesIntrospectionAsync(validated, preferredUsername))
        {
            StampGatewayIdentity(validated, preferredUsername);
        }
    }

    // Sourced from the principal (not from the mint-time HttpContext.Items publication) so it
    // is recorded regardless of what the request does afterwards — success, 401, 403, or a
    // Cyclops forward that never mints an internal token (ADR 0093).
    private static void RecordTokenId(ValidatedToken token)
    {
        if (token.Principal.FindFirstValue(JwtRegisteredClaimNames.Jti) is not { Length: > 0 } jti)
        {
            return;
        }

        token.Activity?.SetTag(AuthTelemetry.TagJti, jti);
        // A missing logging side-channel must never break authentication.
        token.Context.HttpContext.RequestServices.GetService<IDiagnosticContext>()?.Set(AuthTelemetry.TagJti, jti);
        token.Logger.LogTokenAccepted(jti, token.UserId);
    }

    private static void LogValidatedClaims(ValidatedToken token)
    {
        if (!token.Logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var userRoles = string.Join(", ", token.Principal.FindAll(ClaimTypes.Role).Select(r => r.Value));
        token.Logger.LogTokenRolesValidated(token.UserId, userRoles);

        var claimsForLogging = token.Principal.Claims.ToList();
        token.Logger.LogClaims(claimsForLogging.Count, ClaimLogFormatter.Format(claimsForLogging));
    }

    private static void FailMissingSub(ValidatedToken token, string? sub)
    {
        token.Logger.LogMissingRequiredClaims(sub ?? "<null>");

        token.Activity?.SetTag(AuthTelemetry.TagError, "missing_required_claims");
        // context.Fail does not raise OnAuthenticationFailed, so the classification is stored here.
        token.Context.HttpContext.Items[ProblemResponse.AuthenticationFailureItemKey] = new AuthenticationFailure(
            StatusCodes.Status422UnprocessableEntity, "auth.token_missing_claims",
            "Token missing required claims", "Token does not carry the required 'sub' claim.");
        token.Context.Fail("Missing required claims: sub and/or preferred_username");
    }

    private static string? BearerTokenOf(HttpContext httpContext)
        => (httpContext.Items[BearerTokenItemKey] as string)?.Split(' ')[1];

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Fail-closed: any introspection failure rejects the token.")]
    private static async Task<bool> PassesIntrospectionAsync(ValidatedToken token, string preferredUsername)
    {
        var bearerToken = BearerTokenOf(token.Context.HttpContext);
        if (string.IsNullOrEmpty(bearerToken))
        {
            return true;
        }

        var introspectionService = token.Context.HttpContext.RequestServices.GetRequiredService<ITokenIntrospectionService>();
        try
        {
            return await ApplyIntrospectionAsync(introspectionService, token, preferredUsername, bearerToken);
        }
        catch (Exception ex)
        {
            token.Logger.LogIntrospectionFailed(ex, token.UserId);
            token.Activity?.SetTag(AuthTelemetry.TagError, "introspection_error");
            // Fail-closed: reject token if introspection fails
            token.Context.Fail("Token introspection failed");
            return false;
        }
    }

    private static async Task<bool> ApplyIntrospectionAsync(ITokenIntrospectionService introspectionService, ValidatedToken token, string preferredUsername, string bearerToken)
    {
        var introspectionResult = await introspectionService.IntrospectAsync(bearerToken, token.Context.HttpContext.RequestAborted);

        if (!introspectionResult.Active)
        {
            FailInactiveToken(token);
            return false;
        }

        token.Logger.LogIntrospectionPassed(token.UserId);

        Claim[] introspected = [.. IntrospectedPreferredUsername(introspectionResult, preferredUsername), .. IntrospectedRoles(introspectionResult)];
        if (introspected.Length > 0)
        {
            token.Principal.AddIdentity(new ClaimsIdentity(introspected));
        }

        return true;
    }

    private static void FailInactiveToken(ValidatedToken token)
    {
        token.Logger.LogTokenInactive(token.UserId);
        token.Activity?.SetTag(AuthTelemetry.TagError, "token_inactive_per_introspection");
        // 401 (not 422): the token is well-formed, only no longer active — a refresh may succeed.
        token.Context.HttpContext.Items[ProblemResponse.AuthenticationFailureItemKey] = new AuthenticationFailure(
            StatusCodes.Status401Unauthorized, "auth.token_revoked",
            "Token revoked", "Access token is no longer active. Obtain a new token.");
        token.Context.Fail("Token is not active according to introspection endpoint");
    }

    private static IEnumerable<Claim> IntrospectedPreferredUsername(IntrospectionResult result, string? preferredUsername)
    {
        if (string.IsNullOrWhiteSpace(preferredUsername) &&
            result.Claims.TryGetValue("preferred_username", out var values) &&
            values.Length > 0)
        {
            yield return new Claim(JwtRegisteredClaimNames.PreferredUsername, values[0]);
        }
    }

    private static IEnumerable<Claim> IntrospectedRoles(IntrospectionResult result)
        => result.Claims.TryGetValue("role", out var roles)
            ? roles.Select(role => new Claim(ClaimTypes.Role, role))
            : [];

    private static void StampGatewayIdentity(ValidatedToken token, string preferredUsername)
    {
        // Gateway-stamped identity; the auth method claim lets downstream detect the origin
        Claim[] claims =
        [
            new (JwtRegisteredClaimNames.Sub, token.UserId),
            new (JwtRegisteredClaimNames.PreferredUsername, preferredUsername),
            new (JwtRegisteredClaimNames.Email, token.Principal.FindFirstValue(JwtRegisteredClaimNames.Email) ?? string.Empty),
            new (ClaimTypes.Role, ""),
            new (EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken))
        ];

        // The identity below is appended, and FindFirstValue reads identities in order, so a claim
        // the IdP token asserts about the caller's own identity would outrank the Gateway's. Core
        // keys its service-key-only provider write gate on "am" (D9) and kill-switch authority on
        // "provider_id", and the transform resolves "provider_id" into the provider_name that
        // decides whose data is read — so a forged pair buys the provider write surface and another
        // provider's scope. Only the Gateway knows how the caller authenticated and which provider
        // it may act for: its values are the authoritative ones.
        if (!TryDiscardSelfAssertedIdentityClaims(token.Principal, out var strippedClaims, out var survivingClaim))
        {
            token.Logger.LogSelfAssertedClaimNotRemoved(survivingClaim.Type, token.UserId, token.Context.Request.Path.Value);

            token.Activity?.SetTag(AuthTelemetry.TagError, "self_asserted_claim_not_removed");
            token.Context.Fail("Self-asserted identity claim could not be removed");
            return;
        }

        foreach (var discarded in strippedClaims)
        {
            token.Logger.LogSelfAssertedClaimDiscarded(discarded.Type, token.UserId, token.Context.Request.Path.Value);
        }

        token.Principal.AddIdentity(new ClaimsIdentity(claims));
    }

    /// <summary>
    /// Removes every <see cref="SelfAssertedIdentityClaimTypes"/> claim the incoming token brought with it, so
    /// the Gateway's own values are the only ones a consumer can read. Reports every claim it successfully
    /// removed, so the caller can log a forged claim being discarded — the security-relevant signal here.
    /// Reports the first claim that survived removal — one backed by an external claims provider does —
    /// because a silently retained claim is a forged claim the whole pipeline would then trust.
    /// <paramref name="strippedClaims"/> is meaningful only when this method returns <see langword="true"/>;
    /// on failure it holds whatever was removed before the unremovable claim was hit, which the token-failing
    /// caller does not consume.
    /// </summary>
    private static bool TryDiscardSelfAssertedIdentityClaims(
        ClaimsPrincipal principal,
        out IReadOnlyList<Claim> strippedClaims,
        [NotNullWhen(false)] out Claim? survivingClaim)
    {
        var stripped = new List<Claim>();
        strippedClaims = stripped;

        foreach (var identity in principal.Identities)
        {
            foreach (var selfAsserted in SelfAssertedIdentityClaimTypes.SelectMany(identity.FindAll).ToList())
            {
                if (!identity.TryRemoveClaim(selfAsserted))
                {
                    survivingClaim = selfAsserted;
                    return false;
                }

                stripped.Add(selfAsserted);
            }
        }

        survivingClaim = null;
        return true;
    }
}