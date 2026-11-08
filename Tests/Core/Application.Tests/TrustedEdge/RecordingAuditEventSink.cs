using System.Collections.Concurrent;

using Lukdrasil.StepUpLogging;

namespace UserOrganizationManagement.Application.Tests.TrustedEdge;

internal sealed class RecordingAuditEventSink : IAuditEventSink
{
    public ConcurrentQueue<AuditEvent> Writes { get; } = new();

    public ValueTask<AuditWriteResult> WriteAsync(AuditEvent auditEvent)
    {
        Writes.Enqueue(auditEvent);
        return ValueTask.FromResult(AuditWriteResult.Stored);
    }
}