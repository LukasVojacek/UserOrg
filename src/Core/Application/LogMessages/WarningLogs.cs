using Microsoft.Extensions.Logging;

namespace UserOrganizationManagement.Application.LogMessages;

public static partial class WarningLogs
{
    [LoggerMessage(EventId = (int)FeatureEventId.Keycloak, Level = LogLevel.Warning, Message = "Invalid header value. \"{headerName}\"")]
    public static partial void InvalidKeycloakHeader(this ILogger logger, string headerName);

    [LoggerMessage(EventId = (int)FeatureEventId.User, Level = LogLevel.Warning, Message = "KeycloakInfo object contains invalid data: {keycloakInfo}")]
    public static partial void InvalidKeycloakInfo(this ILogger logger, object? keycloakInfo);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Record not found")]
    public static partial void RecordNotFound(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Project not found")]
    public static partial void ProjectNotFound(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "User not found")]
    public static partial void UserNotFound(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Role not found")]
    public static partial void RoleNotFound(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "System permission type not found")]
    public static partial void SystemPermissionNotFound(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Attempt to use/modify a read-only record")]
    public static partial void ModificationAttemptOnReadOnlyRecord(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Attribute size was exceeded!")]
    public static partial void RequestSizeExceeded(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Keycloak, Level = LogLevel.Warning, Message = "Invalid OpenID info object: {openIdInfo}")]
    public static partial void InvalidOpenIdInfo(this ILogger logger, object? openIdInfo);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "HTTP {statusCode} for {method} {scheme}://{host}{path}{queryString} (Content-Length: {contentLength})")]
    public static partial void HttpResponseClientError(this ILogger logger, Exception exception, int statusCode, string method, string scheme, string host, string? path, string queryString, long? contentLength);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Validation failed")]
    public static partial void ValidationFailed(this ILogger logger);
    /// <summary>
    /// Logs when an <see cref="ERequestType.ProjectResourceAssignment"/> accept is retried after the resource
    /// assignment was persisted but the request state change was not, allowing the state update to complete
    /// without re-assigning the already-committed resource.
    /// </summary>
    /// <remarks>
    /// Only the swallowed exception's type name is recorded: its message embeds the project short name,
    /// which does not belong in a log record (ADR-0006).
    /// </remarks>
    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Resource of request \"{requestId}\" is already assigned to its project ({swallowedExceptionType}), accepting the request without re-assigning")]
    public static partial void ResourceAlreadyAssignedOnAccept(this ILogger logger, Guid requestId, string swallowedExceptionType);

    /// <summary>
    /// Logs when a <see cref="ERequestType.SelfOnboarding"/> accept is retried after the user assignment and
    /// role grant were persisted but the request state change was not, allowing the state update to complete
    /// without re-assigning the already-committed membership and role.
    /// </summary>
    /// <remarks>
    /// Only the swallowed exception's type name is recorded: its message embeds the IdP subject id and the
    /// project short name, which do not belong in a log record (ADR-0006).
    /// </remarks>
    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "User of request \"{requestId}\" is already assigned to its project with a role ({swallowedExceptionType}), accepting the request without re-assigning")]
    public static partial void UserAlreadyAssignedOnAccept(this ILogger logger, Guid requestId, string swallowedExceptionType);

    /// <summary>
    /// Logs when the rollback of a failed <see cref="ERequestType.ProjectResourceAssignment"/> accept could
    /// not complete - typically because the commit itself failed and had already aborted the transaction.
    /// </summary>
    /// <remarks>
    /// Recorded rather than thrown: disposing the transaction still rolls it back, so this is a diagnostic,
    /// and rethrowing it would hide the failure that caused the rollback in the first place.
    /// </remarks>
    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Warning, Message = "Rollback of the failed accept of request \"{requestId}\" did not complete; its disposal rolls it back")]
    public static partial void RollbackAfterFailedAcceptIncomplete(this ILogger logger, Exception rollbackFailure, Guid requestId);

}