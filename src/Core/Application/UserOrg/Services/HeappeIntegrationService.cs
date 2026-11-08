using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.Services;

/// <summary>
/// Provides methods for integrating with HEAppE to retrieve project allocation information for users within an
/// organizational unit context.
/// </summary>
/// <param name="userOrgUnitOfWork">The unit of work used to access user and project data within the organizational unit.</param>
public class HeappeIntegrationService(IUserOrgUnitOfWork userOrgUnitOfWork) : IHeappeIntegrationService
{
    /// <summary>
    /// Retrieves the list of project resource allocations associated with the specified user.
    /// </summary>
    /// <param name="idpUserId">The unique identifier of the user in the identity provider. Cannot be null.</param>
    /// <param name="heappeIdentifier">An optional HEAppE identifier for the user. This parameter is not used in this method.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of project allocation information for the projects the user is assigned to. The list is empty
    /// if the user is not assigned to any projects.</returns>
    public async Task<IList<ProjectAllocationInfo>> GetProjectAllocationsForUserAsync(string idpUserId, string heappeIdentifier, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(idpUserId);
        ArgumentNullException.ThrowIfNull(heappeIdentifier);

        var userId = await userOrgUnitOfWork.UserRepository.GetUserIdByIdpUserIdAsync(idpUserId, ct);
        UserNotFoundException.ThrowIfNull(userId);

        return await userOrgUnitOfWork.ProjectRepository.GetProjectResourceAllocationInfoByUserIdAndHeappeIdentifierAsync(userId.Value, heappeIdentifier, ct);
    }

    /// <summary>
    /// Retrieves the project resource allocation information for a specified user and project, identified by HEAppE ID
    /// and resource name.
    /// </summary>
    /// <param name="projectShortName">The short name of the project for which to retrieve allocation information. Cannot be null.</param>
    /// <param name="resourceName">The name of the resource associated with the project allocation. Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A <see cref="ProjectResourceInfo"/> object containing the allocation details for the specified project and
    /// resource; or <see langword="null"/> if no allocation is found.</returns>
    public async Task<ProjectResourceInfo?> GetProjectResourceAllocationAsync(ProjectShortName projectShortName, string resourceName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectShortName);
        ArgumentNullException.ThrowIfNull(resourceName);

        var projectAllocation = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAndResourceNameAsync(projectShortName, resourceName, ct);
        return projectAllocation;
    }


    /// <summary>
    /// Retrieves the HEAppE instance information for a specified project and HEAppE identifier.
    /// </summary>
    /// <param name="projectShortName">The short name of the project for which to retrieve HEAppE endpoint versions. Cannot be null.</param>
    /// <param name="heappeIdentifier">The identifier of the HEAppE instance to match.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="HeappeInstance"/> object containing the identifier and version if a matching HEAppE instance is
    /// found; otherwise, <see langword="null"/>.</returns>
    public async Task<HeappeInstance?> GetHeappeEndpointVersions(ProjectShortName projectShortName, HeappeIdentifier heappeIdentifier, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectShortName);

        var links = await userOrgUnitOfWork.ProjectRepository.GetHeappeLinks(projectShortName, ct);

        var heappeInstance = links
            .Where(l => l.HEAppEApiIdentifier == heappeIdentifier)
            .Select(h => new HeappeInstance(heappeIdentifier, h.HEAppEApiVersion, h.HEAppEApi))
            .FirstOrDefault();

        return heappeInstance;
    }

    /// <summary>
    /// Retrieves information about the available HEAppE endpoint versions for the specified HEAppE instance.
    /// </summary>
    /// <param name="heappeIdentifier">The identifier of the HEAppE instance for which to retrieve endpoint version information.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="HeappeInstance"/>
    /// object with endpoint version information if found; otherwise, <see langword="null"/>.</returns>
    public async Task<HeappeInstance?> GetHeappeEndpointVersions(HeappeIdentifier heappeIdentifier, CancellationToken ct = default)
    {
        var heappeInstance = await userOrgUnitOfWork.ProjectRepository.GetHeappeVerison(heappeIdentifier, ct);
        return heappeInstance;
    }


    public async Task<Result<HashSet<ProviderLocationInfo>, Error>> GetValidLocationNamesByIds(string projectShortName, string resourceName, IEnumerable<ELocationType> locationTypes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectShortName);
        ArgumentNullException.ThrowIfNull(resourceName);
        ArgumentNullException.ThrowIfNull(locationTypes);

        var shortName = new ProjectShortName(projectShortName);
        var locationTypeIds = locationTypes.Select(lt => (int)lt);

        var projectExists = await userOrgUnitOfWork.ProjectRepository.Query()
            .AnyAsync(p => p.ShortName == shortName &&
                p.AssignedProjectResources.Any(pr => pr.ProjectResource != null && pr.ProjectResource.Name == resourceName),
                cancellationToken);

        if (!projectExists)
        {
            var error = new ErrorBuilder()
                .WithDescription($"Project '{projectShortName}' with resource '{resourceName}' not found.")
                .WithSeverity(ErrorSeverity.Error)
                .Build();
            return Result<HashSet<ProviderLocationInfo>, Error>.Failure(error, State.NotFound);
        }

        var data = await userOrgUnitOfWork.ProjectRepository.Query()
           .Where(p => p.ShortName == shortName)
           .SelectMany(p => p.AssignedProjectResources)
                .Where(pr => pr.ProjectResource != null && pr.ProjectResource.Name == resourceName)
                .SelectMany(pr => pr.ProjectResource!.AssignedLocations)
                    .Where(l => locationTypeIds.Contains(l.Location!.LocationTypeId))
                    .Select(l => new ProviderLocationInfo(
                       l.Location!.Id,
                       l.Location.Name!,
                       l.AggregationName,
                       l.Location.Provider.Name ?? string.Empty))
                   .Distinct()
                   .ToHashSetAsync(cancellationToken);

        return Result<HashSet<ProviderLocationInfo>, Error>.Success(data, State.Ok);
    }


}