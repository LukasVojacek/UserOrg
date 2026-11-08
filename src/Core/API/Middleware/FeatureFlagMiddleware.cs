using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http.Features;
using Microsoft.FeatureManagement;

using UserOrg.Shared;
using UserOrg.Shared.FeatureManagement;

using UserOrganizationManagement.Application.LogMessages;

namespace UserOrganizationManagement.API.Middleware;

/// <summary>
/// Middleware that enforces feature flag restrictions on endpoints.
/// </summary>
/// <remarks>
/// This middleware checks if an endpoint is marked with <see cref="DisabledByFeatureFlagMetadata"/>
/// and returns HTTP 404 Not Found if the associated feature flag is enabled. It also checks for
/// <see cref="EnabledByFeatureFlagMetadata"/> and returns HTTP 404 Not Found if the associated feature
/// flag is disabled. This prevents clients from accessing disabled endpoints and doesn't leak their existence.
/// </remarks>
/// <param name="next">The next request delegate in the pipeline</param>
/// <param name="featureManager">The feature manager for checking feature flags</param>
/// <param name="logger">Logger for audit trail of blocked requests</param>
internal sealed partial class FeatureFlagMiddleware(RequestDelegate next, IFeatureManager featureManager, ILogger<FeatureFlagMiddleware> logger)
{
    private const string AnonymousActorId = "anonymous";

    private const string UserActorType = "user";

    private const string ServiceActorType = "service";

    private const string AnonymousActorType = "anonymous";

    /// <summary>
    /// Invokes the middleware to check feature flag restrictions.
    /// </summary>
    /// <param name="context">The HTTP context</param>
    /// <param name="auditLogger">The per-request audit logger that records each blocked request as Denied, fail-open</param>
    /// <returns>A task representing the asynchronous operation</returns>
    public async Task InvokeAsync(HttpContext context, IAuditLogger<FeatureFlagMiddleware> auditLogger)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpoint = context.GetEndpoint();

        if (endpoint is not null)
        {
            var disabledByFlagMetadata = endpoint.Metadata.GetMetadata<DisabledByFeatureFlagMetadata>();

            if (disabledByFlagMetadata is not null)
            {
                var isFlagEnabled = await featureManager.IsEnabledAsync(disabledByFlagMetadata.FeatureFlagName);

                if (isFlagEnabled)
                {
                    LogBlockedByFeatureFlag(logger, context.Request.Method, context.Request.Path.Value, disabledByFlagMetadata.FeatureFlagName);

                    await AuditBlockedAsync(context, auditLogger, disabledByFlagMetadata.FeatureFlagName);

                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync("Not Found");
                    return;
                }
            }

            var enabledByFlagMetadata = endpoint.Metadata.GetMetadata<EnabledByFeatureFlagMetadata>();

            if (enabledByFlagMetadata is not null)
            {
                var isFlagEnabled = await featureManager.IsEnabledAsync(enabledByFlagMetadata.FeatureFlagName);

                if (!isFlagEnabled)
                {
                    LogBlockedByDisabledFeatureFlag(logger, context.Request.Method, context.Request.Path.Value, enabledByFlagMetadata.FeatureFlagName);

                    await AuditBlockedAsync(context, auditLogger, enabledByFlagMetadata.FeatureFlagName);

                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync("Not Found");
                    return;
                }
            }
        }

        await next(context);
    }

    private async Task AuditBlockedAsync(HttpContext context, IAuditLogger<FeatureFlagMiddleware> auditLogger, string flagName)
    {
        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        var auditEvent = AuditEvent.Denied(HostAuditActions.FeatureFlagRequestBlocked, NormalizeActorId(sub), ResolveActorType(context.User, sub)) with
        {
            Reason = flagName,
            Data = new Dictionary<string, object?>
            {
                ["route"] = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
                ["method"] = context.Request.Method,
                ["sub"] = sub,
            },
        };

        try
        {
            await auditLogger.AuditAsync(auditEvent);
        }
#pragma warning disable CA1031 // why: fail-open, a sink outage must not turn the blocked request's 404 into a 500
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAuditWriteFailed(logger, auditEvent.Action, auditEvent.Outcome, ex.GetType().Name);
        }
    }

    private static string NormalizeActorId(string? sub)
    {
        if (string.IsNullOrEmpty(sub))
        {
            return AnonymousActorId;
        }

        var normalized = sub.Split('@')[0];
        return normalized.Length == 0 ? AnonymousActorId : normalized;
    }

    private static string ResolveActorType(ClaimsPrincipal user, string? sub)
    {
        if (user.FindFirstValue(EAuthorizationMethod.ClaimType) == nameof(EAuthorizationMethod.ApiKey))
        {
            return ServiceActorType;
        }

        return string.IsNullOrEmpty(sub) ? AnonymousActorType : UserActorType;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Endpoint blocked by feature flag: {Method} {Path} - Flag: {FlagName}")]
    private static partial void LogBlockedByFeatureFlag(ILogger logger, string method, string? path, string flagName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Endpoint blocked by disabled feature flag: {Method} {Path} - Flag: {FlagName}")]
    private static partial void LogBlockedByDisabledFeatureFlag(ILogger logger, string method, string? path, string flagName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Best-effort audit write failed: {AuditAction} {AuditOutcome} ({ExceptionType})")]
    private static partial void LogAuditWriteFailed(ILogger logger, string auditAction, AuditOutcome auditOutcome, string exceptionType);
}