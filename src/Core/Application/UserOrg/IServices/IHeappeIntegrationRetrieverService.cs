using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

/// <summary>
/// Defines a service for retrieving HEAppE project allocation information associated with a specific user.
/// </summary>
public interface IHeappeIntegrationService
{
    Task<IList<ProjectAllocationInfo>> GetProjectAllocationsForUserAsync(string idpUserId, string heappeIdentifier, CancellationToken ct);
    Task<ProjectResourceInfo?> GetProjectResourceAllocationAsync(ProjectShortName projectShortName, string resourceName, CancellationToken ct);
    Task<HeappeInstance?> GetHeappeEndpointVersions(HeappeIdentifier heappeIdentifier, CancellationToken ct);
    Task<HeappeInstance?> GetHeappeEndpointVersions(ProjectShortName projectShortName, HeappeIdentifier heappeIdentifier, CancellationToken ct);

    Task<Result<HashSet<ProviderLocationInfo>, Error>> GetValidLocationNamesByIds(string projectShortName, string resourceName, IEnumerable<ELocationType> locationTypes, CancellationToken cancellationToken);

}