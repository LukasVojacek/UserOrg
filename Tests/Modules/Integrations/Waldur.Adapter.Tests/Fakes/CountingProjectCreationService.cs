using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Features.InitializationProjectWithClusters;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IProjectCreationService"/> that succeeds and records each HEAppE
/// project-ensure call (one per HPC resource) so tests can assert per-resource / per-event invocation.
/// </summary>
internal sealed class CountingProjectCreationService : IProjectCreationService
{
    public List<string> EnsuredResourceNames { get; } = [];

    public Task<Result<BaseHeappeConnectionInfo, Error>> GetServiceHeappeConnectionInfoAsync(HeappeIdentifier heappeIdentifier, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    public Task<Result<long, Error>> InitializeHEAppEProjectWithClustersAsync(
        HeappeProjectSpecificationEntry projectSpecification,
        CancellationToken ct)
    {
        EnsuredResourceNames.Add(projectSpecification.ProjectSpecification.Name);
        return Task.FromResult(Result<long, Error>.Success(EnsuredResourceNames.Count));
    }
}
