using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Captures every <see cref="AuditEvent"/> handed to the sink, standing in for the real
/// (immudb-backed) destination so a test can assert on what was written. The response status
/// code is captured alongside it so a test can pin that the write happened before the 403; it is
/// null when no request is in flight. Setting <see cref="ThrowOnWrite"/> makes every write throw
/// it instead, which is how a test proves a write is fail-closed or fail-open.
/// </summary>
internal sealed class RecordingAuditEventSink(IHttpContextAccessor httpContextAccessor) : IAuditEventSink
{
    public List<(AuditEvent Event, int? ResponseStatusCode)> Writes { get; } = [];

    public Exception? ThrowOnWrite { get; set; }

    public ValueTask<AuditWriteResult> WriteAsync(AuditEvent auditEvent)
    {
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        Writes.Add((auditEvent, httpContextAccessor.HttpContext?.Response.StatusCode));
        return ValueTask.FromResult(AuditWriteResult.Stored);
    }
}
