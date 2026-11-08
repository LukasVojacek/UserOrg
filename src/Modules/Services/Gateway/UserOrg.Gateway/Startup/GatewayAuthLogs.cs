namespace UserOrg.Gateway.Startup;

/// <summary>
/// Provides structured logging methods for the Gateway's bearer events and internal token transforms.
/// </summary>
internal static partial class GatewayAuthLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Message received for path {Path}. Has Authorization header: {HasAuth}")]
    internal static partial void LogMessageReceived(this ILogger logger, string? path, bool hasAuth);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Token accepted with jti {Jti} for user {UserId}")]
    internal static partial void LogTokenAccepted(this ILogger logger, string jti, string userId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Claims ({Count}): {Claims}")]
    internal static partial void LogClaims(this ILogger logger, int count, string claims);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "[AUTH:GATEWAY] Token is missing required claims. sub={Sub}.")]
    internal static partial void LogMissingRequiredClaims(this ILogger logger, string sub);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "[AUTH:GATEWAY] Self-asserted claim {ClaimType} could not be removed from the principal of user {UserId} on path {Path}, rejecting the token")]
    internal static partial void LogSelfAssertedClaimNotRemoved(this ILogger logger, string claimType, string userId, string? path);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "[AUTH:GATEWAY] Discarded self-asserted claim {ClaimType} from an inbound token for user {UserId} on path {Path}")]
    internal static partial void LogSelfAssertedClaimDiscarded(this ILogger logger, string claimType, string userId, string? path);

    [LoggerMessage(EventId = 7, Message = "[AUTH:GATEWAY] Authentication failed for path {Path}: {Code} ({ExceptionType}) -> {StatusCode}. Authority: {Authority}")]
    internal static partial void LogAuthenticationFailed(this ILogger logger, LogLevel level, Exception exception, string? path, string code, string exceptionType, int statusCode, string authority);

    [LoggerMessage(EventId = 8, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Challenge triggered for path {Path}. Error: {Error}, ErrorDescription: {ErrorDescription}")]
    internal static partial void LogChallenge(this ILogger logger, string? path, string? error, string? errorDescription);

    [LoggerMessage(EventId = 9, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Access forbidden for path {Path}")]
    internal static partial void LogForbidden(this ILogger logger, string? path);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Public endpoint detected, generating S2S token for {Path}")]
    internal static partial void LogPublicEndpointDetected(this ILogger logger, string? path);

    [LoggerMessage(EventId = 11, Level = LogLevel.Error, Message = "Failed to generate S2S token for public endpoint {Path}")]
    internal static partial void LogS2STokenGenerationFailed(this ILogger logger, string? path);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "S2S token added for public endpoint {Path}")]
    internal static partial void LogS2STokenAdded(this ILogger logger, string? path);

    [LoggerMessage(EventId = 13, Level = LogLevel.Error, Message = "TokenService is null, cannot generate internal token for path {Path}")]
    internal static partial void LogTokenServiceMissing(this ILogger logger, string? path);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning, Message = "No Authorization header for authenticated user on path {Path}")]
    internal static partial void LogNoAuthorizationHeader(this ILogger logger, string? path);

    [LoggerMessage(EventId = 15, Level = LogLevel.Information, Message = "[AUTH:GATEWAY] Bearer authority configured: {Authority}")]
    internal static partial void LogBearerAuthorityConfigured(this ILogger logger, string? authority);

    [LoggerMessage(EventId = 16, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Token introspection passed for user {UserId}")]
    internal static partial void LogIntrospectionPassed(this ILogger logger, string userId);

    [LoggerMessage(EventId = 17, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Token validated for user {UserId} with roles {UserRoles}")]
    internal static partial void LogTokenRolesValidated(this ILogger logger, string userId, string userRoles);

    [LoggerMessage(EventId = 18, Level = LogLevel.Warning, Message = "[AUTH] Token marked as inactive by introspection endpoint for user {UserId}")]
    internal static partial void LogTokenInactive(this ILogger logger, string userId);

    [LoggerMessage(EventId = 19, Level = LogLevel.Error, Message = "[AUTH:GATEWAY] Introspection failed for user {UserId}")]
    internal static partial void LogIntrospectionFailed(this ILogger logger, Exception exception, string userId);

    [LoggerMessage(EventId = 20, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Full exception details")]
    internal static partial void LogAuthenticationFailureDetail(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 21, Level = LogLevel.Debug, Message = "[AUTH:GATEWAY] Inner exception: {InnerExceptionType}, Message: {InnerMessage}")]
    internal static partial void LogAuthenticationInnerException(this ILogger logger, string innerExceptionType, string innerMessage);
}