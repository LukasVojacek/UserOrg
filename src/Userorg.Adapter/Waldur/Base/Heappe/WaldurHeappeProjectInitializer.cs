using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Init;
using Heappe.Contracts.HEappeApiDto.Requests;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.InitializationProjectWithClusters;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Domain.Entities;

namespace Userorg.Adapter.Waldur.Base.Heappe;

internal static class WaldurHeappeProjectInitializer
{
    private const int _DefaultProjectPrefferedAuthTypeId = 11;
    /// <summary>
    /// Initializes a HEAppE project for the given resource if a local base path is configured.
    /// Returns <c>Success(true)</c> if initialization is not required or succeeds.
    /// </summary>
    /// <param name="dbAssignedLocation">The assigned location data containing HEAppE configuration.</param>
    /// <param name="providerId">The external provider ID.</param>
    /// <param name="prefferedAuthType">The preferred authentication type for the HEAppE project.</param>
    /// <param name="piEmail">The Principal Investigator email to stamp on the HEAppE project specification.</param>
    /// <param name="heappeProjectCreationService">Service for creating HEAppE projects with clusters.</param>
    /// <param name="heappeService">Service for HEAppE operations.</param>
    /// <param name="connectionFactory">Resolves the HEAppE instance behind the NexusApi proxy by its identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task<Result<bool, Error>> InitializeIfNeededAsync(
        AssignedLocation dbAssignedLocation,
        string providerId,
        string prefferedAuthType,
        string piEmail,
        IProjectCreationService heappeProjectCreationService,
        IHeappeService heappeService,
        IHeappeConnectionFactory connectionFactory,
        CancellationToken ct)
    {

        if (IsMissingRequiredData(dbAssignedLocation))
        {
            return Result<bool, Error>.Failure(Errors.MissingResourceData(providerId));
        }

        if (HasInvalidLocationEndpoint(dbAssignedLocation))
        {
            return Result<bool, Error>.Failure(Errors.MissingLocationEndpoint(dbAssignedLocation.ProjectResource.Name, providerId));
        }

        var dbResource = dbAssignedLocation.ProjectResource;
        var assignedValues = dbAssignedLocation.AssignedLocationsValues.ToList();

        var heappeUri = assignedValues?.FirstOrDefault(x => x.Name == ApplicationConstants.HeappeUriKey)?.Value;
        var rawIdentifier = assignedValues?.FirstOrDefault(x => x.Name == ApplicationConstants.HeappeIdentifierKey)?.Value;
        if (!HeappeIdentifier.TryParse(rawIdentifier, out var heappeIdentifier))
        {
            return Result<bool, Error>.Failure(Errors.HeappeInstanceNotConfigured(dbResource.Name, providerId));
        }

        var localBasePath = assignedValues?.FirstOrDefault(x => x.Name == ApplicationConstants.HEAppELocalBasePathKey);
        if (localBasePath is null || string.IsNullOrEmpty(localBasePath.Value))
        {
            return Result<bool, Error>.Failure(Errors.MissingLocalBasePath(dbResource.Name, providerId));
        }

        var heappeUsageType = dbAssignedLocation.AllocationUnitType?.Name switch
        {
            "core-hours" => EUsageType.CoreHours,
            "node-hours" => EUsageType.NodeHours,
            _ => EUsageType.Unspecified
        };

        if (heappeUsageType == EUsageType.Unspecified)
        {
            return Result<bool, Error>.Failure(Errors.UnspecifiedAllocationUnitType(dbResource.Name, providerId, dbAssignedLocation.AllocationUnitType?.Name));
        }

        var publicConnection = await connectionFactory.ForPublicAsync(heappeIdentifier, ct);
        if (!publicConnection.IsSuccess)
        {
            return Result<bool, Error>.Failure(publicConnection.Error, publicConnection.State);
        }

        var authTypes = await heappeService.GetClusterAuthenticationCredentialsAuthTypesAsync(publicConnection.Value, ct);
        ClusterAuthenticationCredentialAuthType? authType = null;

        if (authTypes is { IsSuccess: false, Error.Severity: ErrorSeverity.Error })
        {
            return Result<bool, Error>.Failure(Errors.FailedToRetrieveAuthTypes);
        }

        var preferebleAuthTypeId = _DefaultProjectPrefferedAuthTypeId;
        // Invalid when the HEAppE version is not supported
        if (authTypes is { IsSuccess: true, State: not State.Invalid })
        {
            authType = authTypes.Value.FirstOrDefault(x => x.Name.Equals(prefferedAuthType, StringComparison.OrdinalIgnoreCase));

            if (authType == null)
            {
                // not found in retrieved types the one that is needed for project initialization, so return failure
                return Result<bool, Error>.Failure(Errors.PreferredAuthTypeNotFound(prefferedAuthType));
            }

            preferebleAuthTypeId = authType.Id;
        }

        var spec = new HeappeProjectSpecificationEntry(
            Uri.TryCreate(heappeUri, UriKind.Absolute, out var uri) ? uri.ToNormalizeUrl() : null,
            heappeIdentifier,
            new ProjectSpecificationEntry(dbResource.Name, dbResource.Name, dbResource.Description, dbResource.StartDate.Value, dbResource.EndDate.Value, heappeUsageType, true, piEmail, true, preferebleAuthTypeId),
            new ClusterSpecificationEntry(dbAssignedLocation.Location.Endpoint!, localBasePath.Value, localBasePath.Value, new AllocationSpecificationEntry(dbAssignedLocation.AggregationName, dbAssignedLocation.AllocationUnitCount ?? 0))
        );

        var result = await heappeProjectCreationService.InitializeHEAppEProjectWithClustersAsync(spec, ct);
        if (!result.IsSuccess)
        {
            return Result<bool, Error>.Failure(result.Error, result.State);
        }

        return Result<bool, Error>.Success(true);
    }

    private static bool HasInvalidLocationEndpoint(AssignedLocation dbAssignedLocation) => dbAssignedLocation.Location.IsDeleted
        && dbAssignedLocation.Location.LocationTypeId != (int)ELocationType.HPC
        && dbAssignedLocation.Location.LocationTypeId != (int)ELocationType.Quantum
        && string.IsNullOrEmpty(dbAssignedLocation.Location.Endpoint);
    private static bool IsMissingRequiredData(AssignedLocation dbAssignedLocation) => dbAssignedLocation.Location is null
        || dbAssignedLocation.ProjectResource is null
        || string.IsNullOrEmpty(dbAssignedLocation.ProjectResource.Name)
        || dbAssignedLocation.ProjectResource.StartDate is null
        || dbAssignedLocation.ProjectResource.EndDate is null
        || dbAssignedLocation.AssignedLocationsValues is null
        || dbAssignedLocation.AssignedLocationsValues.Count == 0;
}