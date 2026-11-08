using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Features.InitializationProjectWithClusters;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IProjectCreationService"/> that always throws — models a HEAppE
/// failure so tests can assert the terminal <c>SaveChangesAsync</c> is never reached (nothing
/// committed, event not acked; ADR-0057/A3).
/// </summary>
internal sealed class ThrowingProjectCreationService : IProjectCreationService
{
    public int CallCount { get; private set; }

    public Task<Result<BaseHeappeConnectionInfo, Error>> GetServiceHeappeConnectionInfoAsync(HeappeIdentifier heappeIdentifier, CancellationToken ct)
    {
        CallCount++;
        throw new InvalidOperationException("HEAppE unavailable.");
    }

    public Task<Result<long, Error>> InitializeHEAppEProjectWithClustersAsync(
        HeappeProjectSpecificationEntry projectSpecification,
        CancellationToken ct)
    {
        CallCount++;
        throw new InvalidOperationException("HEAppE unavailable.");
    }
}
