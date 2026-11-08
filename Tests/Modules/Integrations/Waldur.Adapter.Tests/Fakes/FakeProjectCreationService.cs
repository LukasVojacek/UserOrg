using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Features.InitializationProjectWithClusters;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IProjectCreationService"/> that always returns success
/// without performing any real HTTP calls.
/// </summary>
internal sealed class FakeProjectCreationService : IProjectCreationService
{
    public Task<Result<BaseHeappeConnectionInfo, Error>> GetServiceHeappeConnectionInfoAsync(HeappeIdentifier heappeIdentifier, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public Task<Result<long, Error>> InitializeHEAppEProjectWithClustersAsync(
        HeappeProjectSpecificationEntry projectSpecification,
        CancellationToken ct)
        => Task.FromResult(Result<long, Error>.Success(1L));
}
