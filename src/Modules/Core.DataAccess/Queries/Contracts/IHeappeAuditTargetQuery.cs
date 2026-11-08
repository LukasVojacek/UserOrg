namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Read port for the HEAppE resource validity audit (ADR-0025). Lists every audit target: a currently-valid,
/// non-deleted <c>ProjectResource</c> assigned at an HPC or Quantum location that carries a HEAppE URL and
/// identifier, paired with the owning project's short name. Implemented over the modular Core read side.
/// </summary>
public interface IHeappeAuditTargetQuery
{
    /// <summary>
    /// Returns one row per (resource, owning project, HEAppE instance). A resource linked to several projects
    /// yields one row per project; the caller deduplicates by (resource name, HEAppE URL) where appropriate.
    /// </summary>
    Task<List<HeappeAuditTargetRow>> GetAuditTargetsAsync(CancellationToken ct);
}