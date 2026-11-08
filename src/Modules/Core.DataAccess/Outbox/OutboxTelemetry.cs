using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Core.DataAccess.Outbox;

/// <summary>
/// Process-scoped OpenTelemetry instrumentation for the outbox dispatcher (ADR-0054 D1). Exposes counters for
/// dispatched / failed / dead-lettered rows, a dispatch-duration histogram, and an <see cref="ActivitySource"/>
/// for a span per <c>ProcessRowAsync</c>. Follows the static <see cref="Meter"/> idiom used elsewhere
/// (HEAppE <c>ProjectCreationService</c>, Waldur <c>SyncTelemetry</c>) — no new telemetry framework. Registered
/// with OTEL via <c>AddMeter</c> / <c>AddSource</c> in <c>UserOrg.ServiceDefaults</c>.
/// </summary>
internal static class OutboxTelemetry
{
    /// <summary>Meter + ActivitySource name; the value registered with OTEL in ServiceDefaults.</summary>
    internal const string MeterName = "UserOrg.Core.Outbox";
    internal const string ActivitySourceName = "UserOrg.Core.Outbox";
    private const string Version = "1.0.0";

    private static readonly Meter s_meter = new(MeterName, Version);

    /// <summary>Span per dispatched row; tagged with message id, type, and attempt count.</summary>
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    internal const string ActivityDispatchRow = "outbox.dispatch";
    internal const string TagMessageId = "outbox.message.id";
    internal const string TagMessageType = "outbox.message.type";
    internal const string TagAttempts = "outbox.message.attempts";

    internal static readonly Counter<long> Dispatched = s_meter.CreateCounter<long>(
        "outbox.messages.dispatched", description: "Outbox rows successfully dispatched to completion.");

    internal static readonly Counter<long> Failed = s_meter.CreateCounter<long>(
        "outbox.messages.failed", description: "Outbox dispatch attempts that failed (before dead-lettering).");

    internal static readonly Counter<long> DeadLettered = s_meter.CreateCounter<long>(
        "outbox.messages.dead_lettered", description: "Outbox rows dead-lettered after reaching MaxAttempts.");

    internal static readonly Histogram<double> DispatchDuration = s_meter.CreateHistogram<double>(
        "outbox.dispatch.duration", unit: "ms", description: "Duration of a single outbox row dispatch attempt.");
}