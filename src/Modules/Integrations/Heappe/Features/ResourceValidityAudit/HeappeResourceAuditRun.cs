using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;

namespace Heappe.Features.ResourceValidityAudit;

/// <summary>Process-scoped metrics for the resource validity audit.</summary>
internal static class HeappeResourceAuditTelemetry
{
    internal const string MeterName = "Heappe.ResourceValidityAudit";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    internal static readonly Counter<long> TargetsChecked = Meter.CreateCounter<long>("heappe_audit_targets_checked_total", description: "Audit targets processed.");
    internal static readonly Counter<long> Findings = Meter.CreateCounter<long>("heappe_audit_findings_total", description: "Audit findings by status.");
    internal static readonly Counter<long> WebhookFailures = Meter.CreateCounter<long>("heappe_audit_webhook_failures_total", description: "Failed webhook notifications.");
}

/// <summary>
/// Shared run body for both the scheduled worker and the manual endpoint's webhook mode: drain the audit stream,
/// keep the findings, and notify if there are any. One source of truth for "run and notify".
/// </summary>
internal static class HeappeResourceAuditRun
{
    internal static async Task<int> CollectAndNotifyAsync(
        IHeappeResourceAuditService audit,
        HeappeResourceAuditWebhook webhook,
        ILogger logger,
        CancellationToken ct)
    {
        var findings = new List<TargetAuditResult>();
        var checkedCount = 0;

        await foreach (var result in audit.RunAuditAsync(ct))
        {
            checkedCount++;
            if (result.IsFinding)
            {
                findings.Add(result);
                HeappeResourceAuditTelemetry.Findings.Add(1, new KeyValuePair<string, object?>("status", result.Status.ToString()));
            }
        }

        HeappeResourceAuditTelemetry.TargetsChecked.Add(checkedCount);
        logger.LogInformation("HEAppE resource audit checked {Checked} target(s); {Findings} finding(s).", checkedCount, findings.Count);

        if (findings.Count > 0)
        {
            await webhook.TryPostAsync(findings, ct);
        }

        return findings.Count;
    }
}