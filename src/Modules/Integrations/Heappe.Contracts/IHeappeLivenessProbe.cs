using UserOrg.Shared;

namespace Heappe.Contracts;

/// <summary>
/// Liveness check against a HEAppE instance's <c>heappe/Health</c> endpoint, used to gate expensive data calls.
/// Segregated from a version resolver on purpose: only the remote, network-backed resolver can answer it — a
/// database-backed version resolver has no notion of reachability and must not be forced to.
/// </summary>
public interface IHeappeLivenessProbe
{
    /// <summary>
    /// Returns <c>true</c> only if the instance answers its health endpoint with a success status within the
    /// health-probe timeout. Any non-success status, timeout, or connection error yields <c>false</c> (treat the
    /// instance as down). Unlike version resolution, a failure does NOT fall back to the database — a failed
    /// liveness check means the subsequent data calls would fail too, which is exactly what the gate avoids.
    /// </summary>
    Task<bool> IsInstanceAliveAsync(HeappeIdentifier identifier, CancellationToken cancellationToken);
}