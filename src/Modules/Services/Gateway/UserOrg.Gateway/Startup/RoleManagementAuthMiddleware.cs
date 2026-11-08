using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

using UserOrg.Gateway.Features.RoleManagement;

namespace UserOrg.Gateway.Startup;

/// <summary>
/// Middleware that handles authentication for the /role-management endpoint.
/// Validates access tokens from ?t= query parameter, checks admin permissions,
/// and issues a cookie for subsequent access.
/// </summary>
internal sealed partial class RoleManagementAuthMiddleware
{
    private const string ConsolePath = "/role-management";

    private readonly RequestDelegate _next;
    private readonly ILogger<RoleManagementAuthMiddleware> _logger;
    private readonly RoleManagementOptions _options;

    public RoleManagementAuthMiddleware(
        RequestDelegate next,
        IOptions<RoleManagementOptions> options,
        ILogger<RoleManagementAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context, IAuditLogger<RoleManagementAuthMiddleware> auditLogger)
    {
        var fullPath = FullPathOf(context.Request);

        // Only handle /role-management requests. Accept either the direct path or a prefixed deployment (e.g. /userorg/role-management).
        if (!IsConsoleRequest(context.Request, fullPath, ConsolePathUnder(_options.PathPrefix)))
        {
            await _next(context);
            return;
        }

        var cookieResult = await context.AuthenticateAsync(Constants.RoleManagementCookieSchemeId);
        if (cookieResult?.Succeeded == true)
        {
            await _next(context);
            return;
        }

        await ChallengeConsoleRequestAsync(context, auditLogger, fullPath);
    }

    private async Task ChallengeConsoleRequestAsync(HttpContext context, IAuditLogger<RoleManagementAuthMiddleware> auditLogger, string fullPath)
    {
        if (HasConsoleCookie(context.Request))
        {
            // Cookie exists but is invalid/expired - return 401 instead of 404
            LogCookieExpired(_logger, fullPath);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Authentication expired. Please provide a valid access token.");
            return;
        }

        // No valid cookie - check for token in ?t= query parameter only
        var token = context.Request.Query["t"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
        {
            // No token provided - pretend endpoint doesn't exist
            LogUnauthorizedWithoutToken(_logger, fullPath);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!TokenMatches(token, _options.AccessToken))
        {
            await auditLogger.AuditAsync(AuditEvent.Denied(GatewayAuditActions.RoleManagementSignedIn, RoleManagementSessionAudit.AnonymousActor, RoleManagementSessionAudit.AnonymousActor) with
            {
                Reason = GatewayAuditActions.DenialReasons.RoleManagementSignInFailed,
            });

            // Invalid token - pretend endpoint doesn't exist (security through obscurity)
            LogUnauthorizedInvalidToken(_logger, fullPath, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await SignInOperatorAsync(context, auditLogger, token);

        context.Response.Redirect(RedirectTargetFor(context.Request));
    }

    private async Task SignInOperatorAsync(HttpContext context, IAuditLogger<RoleManagementAuthMiddleware> auditLogger, string token)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var userId = $"rm-{tokenHash[..16]}";
        var adminName = $"admin-{tokenHash[..8]}";
        var rmAccessId = Guid.NewGuid().ToString("N");

        var cookieClaims = new List<Claim>
        {
            // Required claim for authorization policy
            new(Constants.RoleManagementAccessIdClaim, rmAccessId),

            // Standard identity claims matching production pattern from Program.cs
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.PreferredUsername, adminName),
            new(JwtRegisteredClaimNames.Email, $"{adminName}@role-management.local"),
            new(ClaimTypes.Role, "Admin"),
            new(ClaimTypes.Name, adminName),
        };

        var cookiePrincipal = new ClaimsPrincipal(new ClaimsIdentity(cookieClaims, Constants.RoleManagementCookieSchemeId));

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(_options.CookieExpirationMinutes),
            AllowRefresh = true
        };

        await auditLogger.AuditAsync(AuditEvent.Success(GatewayAuditActions.RoleManagementSignedIn, userId, RoleManagementSessionAudit.OperatorActorType) with
        {
            Data = new Dictionary<string, object?> { [Constants.RoleManagementAccessIdClaim] = rmAccessId },
        });

        await context.SignInAsync(Constants.RoleManagementCookieSchemeId, cookiePrincipal, authProperties);

        // Set the authenticated user in the current context so authorization policy can see the claims
        context.User = cookiePrincipal;
    }

    private bool HasConsoleCookie(HttpRequest request) => request.Cookies.ContainsKey(_options.CookieName);

    private string RedirectTargetFor(HttpRequest request) => ConsolePathUnder(DeploymentPrefixOf(request));

    // Preserve PathBase if present; otherwise use configured PathPrefix or infer a prefix from the request path when possible.
    private string? DeploymentPrefixOf(HttpRequest request)
    {
        if (request.PathBase.HasValue)
        {
            return request.PathBase.Value;
        }

        return string.IsNullOrWhiteSpace(_options.PathPrefix) ? InferredPrefix(request.Path.Value) : _options.PathPrefix;
    }

    private static string? InferredPrefix(string? path)
    {
        var index = path?.IndexOf(ConsolePath, StringComparison.OrdinalIgnoreCase) ?? -1;
        return index > 0 ? path![..index] : null;
    }

    private static string ConsolePathUnder(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return ConsolePath;
        }

        var normalized = prefix.StartsWith('/') ? prefix : $"/{prefix}";
        return normalized.TrimEnd('/') + ConsolePath;
    }

    private static string FullPathOf(HttpRequest request) => (request.PathBase + request.Path).Value ?? string.Empty;

    private static bool IsConsoleRequest(HttpRequest request, string fullPath, string expected) =>
        request.Path.StartsWithSegments(expected, StringComparison.OrdinalIgnoreCase) ||
        fullPath.StartsWith(expected, StringComparison.OrdinalIgnoreCase);

    private static bool TokenMatches(string presented, string? configured)
        => !string.IsNullOrEmpty(configured)
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(presented)), SHA256.HashData(Encoding.UTF8.GetBytes(configured)));

    [LoggerMessage(Level = LogLevel.Debug, Message = "Role management cookie expired or invalid for path {Path}")]
    private static partial void LogCookieExpired(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unauthorized access attempt to {Path} without token")]
    private static partial void LogUnauthorizedWithoutToken(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unauthorized access attempt to {Path} with invalid token from {RemoteIp}")]
    private static partial void LogUnauthorizedInvalidToken(ILogger logger, string path, IPAddress? remoteIp);
}