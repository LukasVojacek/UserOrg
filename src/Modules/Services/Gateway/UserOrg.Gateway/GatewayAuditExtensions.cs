using Lukdrasil.StepUpLogging;

namespace UserOrg.Gateway;

internal static partial class GatewayAuditExtensions
{
    internal static async Task AuditBestEffortAsync<TCategory>(this IAuditLogger<TCategory> auditLogger, AuditEvent auditEvent, ILogger logger)
    {
        try
        {
            await auditLogger.AuditAsync(auditEvent);
        }
#pragma warning disable CA1031 // Fail-open by design (D10): no sink failure may reach the caller.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogAuditWriteFailed(logger, auditEvent.Action, auditEvent.Outcome, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Audit write {Action} ({Outcome}) failed with {ExceptionType}")]
    private static partial void LogAuditWriteFailed(ILogger logger, string action, AuditOutcome outcome, string exceptionType);
}