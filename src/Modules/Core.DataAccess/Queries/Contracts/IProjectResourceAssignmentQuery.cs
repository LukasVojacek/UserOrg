namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Write-validation read for the resource↔project assignment endpoints (F5). Authority-scoped (an
/// Admin / provider actor), not caller-scoped — it answers a cross-aggregate question the
/// <c>Project</c> aggregate cannot see (location sharedness lives under <c>Provider</c>).
/// </summary>
public interface IProjectResourceAssignmentQuery
{
    /// <summary>
    /// Whether the project already holds an active assigned resource that sits on an active <b>non-shared</b>
    /// location. The assign use-case uses this to port the legacy non-shared rule (F5 G6): a new assign is
    /// blocked when an <i>existing</i> assigned resource is non-shared. Faithful to the legacy asymmetry — the
    /// <i>incoming</i> resource is not inspected here. Soft-deleted links and allocations are excluded.
    /// </summary>
    Task<bool> ProjectHasNonSharedResourceAsync(ProjectShortName projectShortName, CancellationToken ct);
}