namespace UserOrg.Gateway.Startup;

/// <summary>
/// Provides structured logging methods for the Cyclops usage forwarding flow.
/// </summary>
internal static partial class CyclopsForwardingLogs
{
    // EventId 1 (LogCyclopsIntegrationNotConfigured) was removed with the unreachable options-null branch it
    // logged for (F3) — the remaining EventIds are not renumbered.

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Cyclops Integration: Failed to retrieve API key(name: Cyclops) from vault: {Error}")]
    internal static partial void LogCyclopsVaultCredentialsRetrievalFailure(this ILogger logger, string error);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Cyclops Integration: API key (Cyclops:ApiKey) in vault is not configured.")]
    internal static partial void LogCyclopsApiKeyNotConfigured(this ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Cyclops Integration: Cyclops forward prepared to {Target}")]
    internal static partial void LogCyclopsForwardPrepared(this ILogger logger, string target);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Cyclops Integration: Error occurred when calling Cyclops API at {Url}")]
    internal static partial void LogCyclopsApiCallError(this ILogger logger, Exception exception, string url);

    // EventIds 6-10 belong to the v1 route's customerdbAPI lookup, which has no v2 counterpart.

    [LoggerMessage(EventId = 6, Level = LogLevel.Error, Message = "Cyclops Integration: Failed to retrieve customer from Cyclops API at {Url}: {StatusCode}")]
    internal static partial void LogCyclopsCustomerLookupFailure(this ILogger logger, string url, int statusCode);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Cyclops Integration: No customer found for project {ProjectShortName}")]
    internal static partial void LogCyclopsCustomerNotFound(this ILogger logger, string projectShortName);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Cyclops Integration: No customer name matches project {ProjectShortName}")]
    internal static partial void LogCyclopsCustomerNameNotMatched(this ILogger logger, string projectShortName);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "Cyclops Integration: Error deserializing the Cyclops customer API response from {Url}")]
    internal static partial void LogCyclopsCustomerResponseDeserializationError(this ILogger logger, Exception exception, string url);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Cyclops Integration: Resolved CustomerId {CustomerId} for project {ProjectShortName}")]
    internal static partial void LogCyclopsCustomerResolved(this ILogger logger, string customerId, string projectShortName);
}