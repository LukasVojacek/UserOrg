using Heappe.Contracts.Entries.Audit;

namespace Heappe.HostContracts;

/// <summary>
/// Host read port for the resource validity audit: lists every audit target (currently-valid resource assigned
/// at an HPC or Quantum location with a HEAppE URL). Implemented host-side over the modular Core read side.
/// </summary>
public interface IHeappeAuditTargetProvider
{
    /// <summary>
    /// Returns the audit targets. Throws on an unexpected read failure (the caller treats that as a failed run).
    /// </summary>
    Task<IList<HeappeAuditTargetEntry>> GetAuditTargetsAsync(CancellationToken ct);
}