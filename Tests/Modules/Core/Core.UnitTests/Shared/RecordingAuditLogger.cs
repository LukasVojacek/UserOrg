using Lukdrasil.StepUpLogging;

using Microsoft.Extensions.Logging;

namespace Core.UnitTests.Shared;

/// <summary>
/// Records every audit event handed to it. With <see cref="ThrowOn"/> set, the write of an event with that
/// outcome is recorded and then throws, reproducing a sink outage.
/// </summary>
internal sealed class RecordingAuditLogger<TCategory> : IAuditLogger<TCategory>
{
    internal const string SinkFailureMessage = "audit sink unavailable";

    internal List<AuditEvent> Events { get; } = [];

    internal AuditOutcome? ThrowOn { get; init; }

    public ValueTask AuditAsync(AuditEvent auditEvent)
    {
        Events.Add(auditEvent);
        if (auditEvent.Outcome == ThrowOn)
            throw new InvalidOperationException(SinkFailureMessage);

        return ValueTask.CompletedTask;
    }

    public ValueTask AuditAsync(AuditEvent auditEvent, Action<ILogger> log) => AuditAsync(auditEvent);
}
