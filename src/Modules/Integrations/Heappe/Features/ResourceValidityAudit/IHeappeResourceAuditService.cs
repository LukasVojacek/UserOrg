namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// Runs the resource validity audit: for each target, verifies the HEAppE project record exists and its validity
/// matches userorg, streaming one result per processed target (Ok included) so callers can render progress or
/// filter to findings. Read-only — it never writes back to HEAppE.
/// </summary>
public interface IHeappeResourceAuditService
{
    IAsyncEnumerable<TargetAuditResult> RunAuditAsync(CancellationToken ct);
}