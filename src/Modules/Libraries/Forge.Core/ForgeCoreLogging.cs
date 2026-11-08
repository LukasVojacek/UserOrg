using Microsoft.Extensions.Logging;

namespace Forge.Core;

internal static partial class ForgeCoreLogging
{
    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Found {PendingCount} pending EF Core migrations. Applying migrations at startup...")]
    public static partial void FoundPendingMigrations(this ILogger logger, int pendingCount);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Applied {PendingCount} EF Core migrations at startup.")]
    public static partial void AppliedMigrations(this ILogger logger, int pendingCount);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "No pending EF Core migrations found at startup.")]
    public static partial void NoPendingMigrations(this ILogger logger);
}