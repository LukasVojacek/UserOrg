using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Logging;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// An <see cref="IAuditLogger{T}"/> for any category that hands each event straight to
/// <see cref="RecordingAuditEventSink"/>. It is for containers built without <c>AddStepUpLogging</c>,
/// whose Serilog registration (an <c>IDiagnosticContext</c> among it) would change what those
/// tests observe. Register it open-generic next to a singleton sink.
/// </summary>
internal sealed class RecordingAuditLogger<T>(RecordingAuditEventSink sink) : IAuditLogger<T>
{
    public async ValueTask AuditAsync(AuditEvent auditEvent) => await sink.WriteAsync(auditEvent);

    public async ValueTask AuditAsync(AuditEvent auditEvent, Action<ILogger> log) => await sink.WriteAsync(auditEvent);
}
