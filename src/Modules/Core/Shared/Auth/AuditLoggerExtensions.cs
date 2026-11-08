using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Logging;

namespace Core.Shared.Auth;

internal static partial class AuditLoggerExtensions
{
    internal static async ValueTask AuditBestEffortAsync<TCategory>(
        this IAuditLogger<TCategory> auditLogger, AuditEvent auditEvent, ILogger logger)
    {
        try
        {
            await auditLogger.AuditAsync(auditEvent);
        }
        catch (Exception ex)
        {
            LogAuditWriteFailed(logger, auditEvent.Action, auditEvent.Outcome, ex.GetType().FullName);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Best-effort audit write {Action} ({Outcome}) failed with {ExceptionType}")]
    private static partial void LogAuditWriteFailed(ILogger logger, string action, AuditOutcome outcome, string? exceptionType);
}