using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Serilog;

using UserOrg.Shared;
using UserOrg.Shared.Http;
using UserOrg.Shared.Logging;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.Authorization;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Configuration;
using UserOrganizationManagement.Domain.ExtModels.OpenId;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.Authentication;

internal static partial class AuthenticationExtensions
{
    public static void AddEmailSenderClient(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient(Domain.Constants.MailSenderHttpClientName, (serviceProvider, opt) =>
        {
            opt.DefaultRequestHeaders.Add("User-Agent", "UserOrg");
            opt.Timeout = TimeSpan.FromSeconds(30);
            opt.BaseAddress = new Uri(builder.Configuration["MailConfiguration:MailSenderUrl"] ?? Environment.GetEnvironmentVariable("MailConfiguration__MailSenderUrl"));
            EmailSenderApiKey.ApplyTo(opt, serviceProvider.GetRequiredService<IOptions<MailConfiguration>>().Value.MailSenderApiKey);
        });
    }
    public static void AddOpenIdAuthServices(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<EndpointAccessConfiguration>().BindConfiguration("EndpointAccessConfiguration");

        var externalAuth = new AuthenticationConfiguration();
        builder.Configuration.GetSection(AuthenticationConfiguration.SectionName).Bind(externalAuth);
        builder.Services.Configure<AuthenticationConfiguration>(builder.Configuration.GetSection(AuthenticationConfiguration.SectionName));

        builder.Services.AddSingleton<EndpointAccessService>();

        // Use static ActivitySource (AuthTelemetry.ActivitySource) per OpenTelemetry best practices

        var host = new Uri(externalAuth.MetadataAddress).Host;

        builder.Services.AddHttpClient("OpenIdUserInfo", opt =>
        {
            opt.DefaultRequestHeaders.Add("User-Agent", "UserOrg");
            opt.DefaultRequestHeaders.Add("Host", host);
            opt.Timeout = TimeSpan.FromSeconds(30);
        });

        // Ensure authentication services are registered so middleware can be activated even if
        // Gateway-specific JwtBearer handler is not configured (avoids runtime activation errors).
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "GatewayJwt";
            options.DefaultChallengeScheme = "GatewayJwt";
            options.DefaultScheme = "GatewayJwt";
        });
        // Ensure authorization services are registered so app.UseAuthorization() can run without error
        builder.Services.AddAuthorization();

        // Minimal: register GatewayJwt handler to validate internal tokens issued by the Gateway (via OIDC discovery/JWKS)
        var gwAuthority = builder.Configuration["GatewayInternal:Authority"] ?? Environment.GetEnvironmentVariable("GatewayInternal__Authority");
        var gwAudience = builder.Configuration["GatewayInternal:Audience"] ?? Environment.GetEnvironmentVariable("GatewayInternal__Audience");

        if (!string.IsNullOrEmpty(gwAuthority) && !string.IsNullOrEmpty(gwAudience))
        {
            builder.Services.AddAuthentication()
                .AddJwtBearer("GatewayJwt", options =>
                {
                    options.Authority = gwAuthority;
                    options.Audience = gwAudience;
                    MapOnlyTheShortRoleClaimToClaimTypesRole(options);
                    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        NameClaimType = JwtRegisteredClaimNames.Sub,
                        RoleClaimType = "role",
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true
                    };

                    options.Events = CreateGatewayJwtBearerEvents(gwAuthority);
                });
        }
    }

    private static void MapOnlyTheShortRoleClaimToClaimTypesRole(JwtBearerOptions options)
    {
        foreach (var handler in options.TokenHandlers.OfType<Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler>())
        {
            handler.MapInboundClaims = true;
            handler.InboundClaimTypeMap = new Dictionary<string, string> { ["role"] = ClaimTypes.Role };
        }
    }

    internal static JwtBearerEvents CreateGatewayJwtBearerEvents(string gwAuthority)
    {
        return new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                using var activity = AuthTelemetry.ActivitySource.StartActivity(AuthTelemetry.ActivityMessageReceived, System.Diagnostics.ActivityKind.Server);
                activity?.SetTag(AuthTelemetry.TagAuthority, gwAuthority);
                activity?.SetTag(AuthTelemetry.TagScheme, ctx.Scheme.Name);
                activity?.SetTag(AuthTelemetry.TagHasAuthorization, !string.IsNullOrEmpty(ctx.Request.Headers.Authorization.ToString()));

                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("APIJwt");
                LogMessageReceived(logger, ctx.Request.Path.Value, !string.IsNullOrEmpty(ctx.Request.Headers.Authorization.ToString()));
                return Task.CompletedTask;
            },
            OnTokenValidated = async ctx =>
            {
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GatewayJwt");
                using var activity = AuthTelemetry.ActivitySource.StartActivity(AuthTelemetry.ActivityTokenValidated, System.Diagnostics.ActivityKind.Server);
                var openIdInfoAvailable = ctx.Principal.TryGetOpenIdUserInfoModel(out var openIdModel);
                if (!openIdInfoAvailable)
                {
                    activity?.SetTag(AuthTelemetry.TagError, "Failed to extract user info from token claims");
                    ctx.Fail("Failed to extract user info from token claims");
                    LogUserInfoExtractionFailed(logger, ctx.Request.Path.Value);
                    return;
                }

                if (openIdModel.Role.Equals(RoleInfoConstants.ProviderAdministratorRoleName, StringComparison.OrdinalIgnoreCase))
                {
                    Guid? providerId = await AAAsync(ctx.Principal, ctx.HttpContext);
                    if (providerId.HasValue && ctx.Principal != null)
                    {
                        var newClaims = ctx.Principal.Claims.Where(x => x.Type != AuthConstants.ProviderIdClaimType).ToList();
                        var providerClaim = new Claim(AuthConstants.ProviderIdClaimType, providerId.Value.ToString());
                        newClaims.Add(providerClaim);

                        var identity = new ClaimsIdentity(newClaims, "GatewayJwt");

                        ctx.Principal = new ClaimsPrincipal(identity);
                    }
                }

                var userId = openIdModel.Sub;
                activity?.SetTag(AuthTelemetry.TagUserId, userId);

                // ADR 0092's original_jti ?? jti fallback: the Gateway relays the caller's jti verbatim into
                // original_jti on the internal token it mints; when there was no incoming token (ApiKey/
                // Cookie/S2S/public) only the internal token's own jti exists. Either way this is the same
                // value the Gateway itself logged, so the chain is joinable across both processes (ADR 0093).
                if ((ctx.Principal.FindFirstValue(AuthConstants.OriginalJtiClaimType) ?? ctx.Principal.FindFirstValue(JwtRegisteredClaimNames.Jti))
                    is { Length: > 0 } jti)
                {
                    activity?.SetTag(AuthTelemetry.TagJti, jti);
                    // A missing logging side-channel must never break authentication.
                    ctx.HttpContext.RequestServices.GetService<IDiagnosticContext>()?.Set(AuthTelemetry.TagJti, jti);
                    LogTokenAccepted(logger, jti, userId);
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    LogTokenValidated(logger, userId, ClaimLogFormatter.Format(ctx.Principal?.Claims ?? []));
                }

                return;
            },
            OnAuthenticationFailed = ctx =>
            {
                using var activity = AuthTelemetry.ActivitySource.StartActivity(AuthTelemetry.ActivityAuthenticationFailed, System.Diagnostics.ActivityKind.Server);
                activity?.SetTag(AuthTelemetry.TagAuthority, gwAuthority);
                activity?.SetTag(AuthTelemetry.TagException, ctx.Exception?.GetType().Name);

                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("APIJwt");
                logger.LogWarning(ctx.Exception, "[AUTH] Gateway JWT validation failed");
                return Task.CompletedTask;
            }
        };
    }

    internal static bool TryGetOpenIdUserInfoModel(this ClaimsPrincipal principal, out OpenIdUserInfoModel openIdUserInfoModel)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal?.Identity == null || !principal.Identity.IsAuthenticated)
        {
            openIdUserInfoModel = null;
            return false;
        }
        openIdUserInfoModel = new OpenIdUserInfoModel()
        {
            Sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
            Name = principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value ?? string.Empty,
            PreferredUsername = principal.FindFirst(JwtRegisteredClaimNames.PreferredUsername)?.Value ?? string.Empty,
            Role = principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty,
            ProviderId = principal.FindFirst(AuthConstants.ProviderIdClaimType)?.Value is string providerIdStr && Guid.TryParse(providerIdStr, out var providerId)
                ? providerId
                : default(Guid?)
        };

        return true;
    }


    public static bool TryGetOpenIdUserInfoModel(this HttpContext context, out OpenIdUserInfoModel openIdUserInfoModel)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
        {
            openIdUserInfoModel = null;
            return false;
        }

        openIdUserInfoModel = new OpenIdUserInfoModel()
        {
            Sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = context.User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? context.User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            Name = context.User.FindFirstValue(JwtRegisteredClaimNames.Name) ?? string.Empty,
            PreferredUsername = context.User.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername) ?? string.Empty,
            Role = context.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            ProviderId = context.User.FindFirstValue(AuthConstants.ProviderIdClaimType) is string providerIdStr && Guid.TryParse(providerIdStr, out var providerId)
                ? providerId
                : default(Guid?)
        };
        return true;
    }

    /// <summary>
    /// Attempts to retrieve an authentication token from the HTTP request headers.
    /// If the custom "X-Api-Token" header is present, its value is used.
    /// Otherwise, extracts the token from the "Authorization" header, assuming the format "Bearer &lt;token&gt;".
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="token">When this method returns, contains the token if found; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if a token was found; otherwise, <c>false</c>.</returns>
    public static bool TryGetAuthToken(this HttpContext context, out string token)
    {
        ArgumentNullException.ThrowIfNull(context);

        var foundXApiToken = context.Request.Headers.TryGetValue(AuthenticationConfiguration.CustomHttpAuthHeaderName, out var xApiToken);
        var foundAuthToken = context.Request.Headers.TryGetValue(AuthenticationConfiguration.DefaultHttpAuthHeaderName, out var authToken);

        var stringifyAuthToken = authToken.ToString();
        if (!string.IsNullOrEmpty(authToken) && !stringifyAuthToken.StartsWith("Bearer "))
        {
            token = null;
            return false;
        }

        token = (foundXApiToken, foundAuthToken) switch
        {
            (false, false) => null,
            (true, false) => xApiToken.ToString(),
            _ => authToken.ToString().Split(' ')[1],
        };

        return !string.IsNullOrEmpty(token);
    }


    /// <summary>
    /// Attempts to extract the original authorization token from the HTTP context's request headers.
    /// </summary>
    /// <remarks>This method checks for the presence of the 'X-Original-Authorization' header in the request
    /// and verifies that its value starts with 'Bearer ' (case-insensitive); an absent or empty header yields <see langword="false"/>. If the header is missing or does not meet these criteria, the
    /// method returns <see langword="false"/> and the output token is <see langword="null"/>.</remarks>
    /// <param name="context">The HTTP context from which to retrieve the original authorization token. This parameter cannot be null.</param>
    /// <param name="token">When this method returns <see langword="true"/>, contains the extracted original authorization token; otherwise,
    /// is <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the original authorization token is successfully retrieved and is not <see
    /// langword="null"/> or empty; otherwise, <see langword="false"/>.</returns>
    public static bool TryGetOriginalAuthToken(this HttpContext context, out string token)
    {
        ArgumentNullException.ThrowIfNull(context);

        const string bearerPrefix = "Bearer ";
        var header = context.Request.Headers[AuthenticationConfiguration.OriginalHttpAuthHeaderName].ToString();

        token = header.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) ? header[bearerPrefix.Length..].Trim() : null;
        return !string.IsNullOrEmpty(token);
    }


    // TODO: Handle as policy in refactored Userorg
    public static bool TryGetIsAdmin(this HttpContext context, out bool isAdmin)
    {
        ArgumentNullException.ThrowIfNull(context);
        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        var defaultAdminRoleName = configuration["DefaultAdminRoleName"] ?? Environment.GetEnvironmentVariable("DefaultAdminRoleName") ?? "Admin";

        isAdmin = context.User.FindFirstValue(ClaimTypes.Role)?.Equals(defaultAdminRoleName, StringComparison.OrdinalIgnoreCase) ?? false;
        return true;
    }

    public static IDictionary<string, HashSet<ProjectShortName>> GetUserProjectRoleNames(this HttpContext context)
    {
        var roles = context.User.FindFirstValue("projectRoles");

        if (string.IsNullOrEmpty(roles))
        {
            return new Dictionary<string, HashSet<ProjectShortName>>();
        }

        try
        {
            var result = JsonSerializer.Deserialize<Dictionary<string, HashSet<ProjectShortName>>>(roles);
            if (result != null)
            {
                foreach (var kvp in result)
                {
                    kvp.Value.RemoveWhere(p => string.IsNullOrEmpty(p.Value));
                }
            }
            return result ?? [];
        }
        catch (JsonException)
        {
            return new Dictionary<string, HashSet<ProjectShortName>>();
        }
    }

    public static Task<Guid?> GetUserInternalId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var idpId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrWhiteSpace(idpId))
        {
            var loggerFac = context.RequestServices.GetRequiredService<ILoggerFactory>();
            var logger = loggerFac.CreateLogger("AuthenticationExtensions");
            logger.LogError("User ID (sub claim) is missing in the token.");
            return null;
        }

        // Normalize idpId: extract prefix before '@' to match how User.IdentityProviderUserId is stored
        var normalizedIdpId = idpId.Split('@')[0];

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var uow = context.RequestServices.GetRequiredService<IUserOrgUnitOfWork>();
        return uow.UserRepository.GetUserIdByIdpUserIdAsync(normalizedIdpId, cts.Token);
    }

    public static bool TryGetProviderId(this HttpContext context, out Guid? providerId)
    {
        ArgumentNullException.ThrowIfNull(context);
        providerId = default;

        var openIdInfoAvailable = context.TryGetOpenIdUserInfoModel(out var openIdModel);
        if (openIdInfoAvailable)
        {
            providerId = openIdModel.ProviderId;
        }

        return openIdInfoAvailable && openIdModel.ProviderId.HasValue;
    }

    public static bool TryGetDataStagingName(this HttpContext context, out string dataStagingName)
    {
        ArgumentNullException.ThrowIfNull(context);
        var serviceType = context.User.FindFirstValue(AuthConstants.ServiceTypeClaimType);

        if (serviceType.Equals(nameof(EServiceType.DDI), StringComparison.OrdinalIgnoreCase))
        {
            dataStagingName = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return true;
        }

        dataStagingName = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the provider ID from the specified HTTP context, based on the user's authentication method.
    /// </summary>
    /// <remarks>This method checks the user's authentication type to determine if it is a bearer token or an
    /// API key. If the authentication type is not supported, it returns null. For API keys, it attempts to parse the
    /// subject claim value as a GUID. For bearer tokens, it retrieves the provider ID using the user service.</remarks>
    /// <param name="principal">The HTTP context containing user information and request services. Must not be null.</param>
    /// <returns>A nullable GUID representing the provider ID if successfully retrieved; otherwise, null.</returns>
    internal static async Task<Guid?> AAAsync(ClaimsPrincipal principal, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subClaimValue = principal.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var authType = principal.FindFirstValue(EAuthorizationMethod.ClaimType);

        var isBearerAuth = authType?.Equals(nameof(EAuthorizationMethod.JwtBearerToken), StringComparison.OrdinalIgnoreCase) ?? false;
        var isProviderApiKey = authType?.Equals(nameof(EAuthorizationMethod.ApiKey), StringComparison.OrdinalIgnoreCase) ?? false;

        if (!(isBearerAuth || isProviderApiKey))
        {
            return default;
        }

        if (isProviderApiKey && string.IsNullOrWhiteSpace(subClaimValue)
            && !Guid.TryParse(subClaimValue, out var parsedProviderId))
        {
            return parsedProviderId;
        }

        if (isBearerAuth)
        {
            // Use cached resolver (60 s TTL) to avoid a DB round-trip on every ProviderAdmin request.
            var resolver = context.RequestServices.GetRequiredService<IProviderIdResolver>();
            return await resolver.GetProviderIdAsync(subClaimValue, context.RequestAborted);
        }

        return default;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[AUTH] Message received for path {Path}. Has Authorization header: {HasAuth}")]
    private static partial void LogMessageReceived(Microsoft.Extensions.Logging.ILogger logger, string? path, bool hasAuth);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[AUTH] Token validated but failed to extract user info from claims for path {Path}")]
    private static partial void LogUserInfoExtractionFailed(Microsoft.Extensions.Logging.ILogger logger, string? path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[AUTH] Token accepted with jti {Jti} for user {UserId}")]
    private static partial void LogTokenAccepted(Microsoft.Extensions.Logging.ILogger logger, string jti, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[AUTH] Token validated for user {UserId}. Claims: {Claims}")]
    private static partial void LogTokenValidated(Microsoft.Extensions.Logging.ILogger logger, string userId, string claims);
}