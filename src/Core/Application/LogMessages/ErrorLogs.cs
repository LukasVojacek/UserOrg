using Microsoft.Extensions.Logging;

namespace UserOrganizationManagement.Application.LogMessages;

public static partial class ErrorLogs
{
    [LoggerMessage(EventId = (int)FeatureEventId.Migration, Level = LogLevel.Error, Message = "Application and database migrations are not the same. Please update the database to the new version. [Actual: {actualMigration}, Latest: {latestMigration}]")]
    public static partial void OutdatedMigration(this ILogger logger, string actualMigration, string latestMigration);

    [LoggerMessage(EventId = (int)FeatureEventId.Migration, Level = LogLevel.Error, Message = "Error occurs in database migrations. {innerExceptionMessage}")]
    public static partial void DbConnectionProblem(this ILogger logger, string innerExceptionMessage);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Error, Message = "Authorization token missing in header")]
    public static partial void AuthTokenMissing(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Error, Message = "HTTP {statusCode} for {method} {scheme}://{host}{path}{queryString} (Content-Length: {contentLength})")]
    public static partial void HttpResponseServerError(this ILogger logger, Exception exception, int statusCode, string method, string scheme, string host, string? path, string queryString, long? contentLength);

    [LoggerMessage(EventId = (int)FeatureEventId.Vault, Level = LogLevel.Error, Message = "Vault access problem")]
    public static partial void VaultAccessForbidden(this ILogger logger);

    [LoggerMessage(EventId = (int)FeatureEventId.General, Level = LogLevel.Error, Message = "Failed to retrieve OpenID user info. Token might be valid but user info retrieval failed.")]
    public static partial void FailedToRetrieveOpenIdInfoLog(this ILogger logger);

    /// <summary>
    /// Logs one diagnostic record for a single entity involved in a concurrency conflict.
    /// Records entity type, table, primary key, and entity state for post-mortem identification of the failed entity,
    /// since <see cref="DbUpdateConcurrencyException"/> root cause is not determinable from application code alone.
    /// No non-key property values are logged; the primary key itself is a surrogate identifier
    /// (Guid/int) for every Core entity today, not a natural/PII-bearing value (ADR-0006).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="entityType">The CLR type name of the entity.</param>
    /// <param name="tableName">The database table name (or placeholder if unavailable).</param>
    /// <param name="key">The serialized primary key value(s).</param>
    /// <param name="entityState">The <see cref="EntityState"/> of the entity (e.g., "Modified", "Deleted").</param>
    /// <param name="correlationId">The W3C trace identifier or request trace identifier for correlation.</param>
    // Diagnostic for issue #265 (PUT /api/Requests/{id}/Accept 500 -> DbUpdateConcurrencyException): the next
    // remote occurrence must identify which entity/key failed. No non-key property values are logged; the
    // primary key is a surrogate identifier for every Core entity today (ADR-0006) - if an entity with a
    // natural key (e-mail, IdP subject) is ever introduced, DescribeKey would surface it and this guarantee
    // would need revisiting.
    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Error, Message = "Concurrency conflict saving {entityType} (table: {tableName}, key: {key}, state: {entityState}). CorrelationId: {correlationId}")]
    public static partial void ConcurrencyConflictEntry(this ILogger logger, string entityType, string tableName, string key, string entityState, string correlationId);

    /// <summary>
    /// Logs a fallback diagnostic record when <see cref="DbUpdateConcurrencyException.Entries"/> is empty.
    /// Ensures every <see cref="DbUpdateConcurrencyException"/> produces exactly one diagnostic record,
    /// even when the exception carries no detailed entry information.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="correlationId">The W3C trace identifier or request trace identifier for correlation.</param>
    // A 409 with no matching log line would teach us nothing about the next remote occurrence
    // (issue #265) - ex.Entries can legitimately be empty, so this fallback guarantees every
    // DbUpdateConcurrencyException still produces exactly one diagnostic record.
    [LoggerMessage(EventId = (int)FeatureEventId.Endpoint, Level = LogLevel.Error, Message = "Concurrency conflict with no reported entries. CorrelationId: {correlationId}")]
    public static partial void ConcurrencyConflictNoEntries(this ILogger logger, string correlationId);
}