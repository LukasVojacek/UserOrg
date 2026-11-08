using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.HEappeApiDto.Responses;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Heappe.Contracts;

public interface IHeappeConnectionService
{
    /// <summary>
    /// Fetches the <b>unpruned</b> cluster catalog for an instance. The HEAppE call carries only
    /// identifier + caller token, so the result is shared by every userorg resource on that instance; all
    /// resource/allowed-template filtering is the caller's responsibility. Cached per identifier and caller token.
    /// </summary>
    ValueTask<Result<IList<ClusterInfoModel>, Error>> GetClusterCatalogAsync(
        string projectShortName, HeappeIdentifier identifier, string bearerToken, CancellationToken ct);

    /// <summary>
    /// Removes the caller's own cached catalog for <paramref name="identifier"/> (the entry keyed by
    /// <paramref name="bearerToken"/>); other callers' entries for the same instance stay.
    /// </summary>
    ValueTask ForgetClusterCatalogAsync(HeappeIdentifier identifier, string bearerToken, CancellationToken ct);

    /// <summary>
    /// The project's active command-template partition set. An empty set means "no partitioning
    /// configured" — the caller keeps all templates. Read once per request.
    /// </summary>
    Task<HashSet<CommandTemplatePartitionResponse>> GetActiveCommandTemplatesAsync(Guid projectId, CancellationToken ct);

    /// <summary>
    /// Cheap fail-fast liveness probe against the instance's <c>heappe/Health</c> endpoint behind the Nexus proxy.
    /// Returns <see langword="true"/> when the instance answers with a success status; <see langword="false"/>
    /// on any other status, a transport failure or a probe timeout.
    /// </summary>
    ValueTask<bool> IsHostAliveAsync(HeappeIdentifier identifier, CancellationToken ct);
}