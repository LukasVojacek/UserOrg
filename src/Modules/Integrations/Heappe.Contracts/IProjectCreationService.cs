using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Heappe.Contracts;

/// <summary>
/// Abstracts HEAppE project initialisation so callers can be tested without real HTTP calls.
/// </summary>
/// <returns>A task representing the asynchronous operation, with the result containing the ID of the created project or an error.</returns>
public interface IProjectCreationService
{
    Task<Result<long, Error>> InitializeHEAppEProjectWithClustersAsync(HeappeProjectSpecificationEntry projectSpecification, CancellationToken ct);

    Task<Result<BaseHeappeConnectionInfo, Error>> GetServiceHeappeConnectionInfoAsync(HeappeIdentifier heappeIdentifier, CancellationToken ct);
}