using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Contracts.Entries;
using Heappe.HostContracts;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Shared;
using UserOrg.Shared.Errors;
using UserOrg.Shared.Extensions.Extensions;

using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ValueObjects;

using ProjectResourceInfoSyncEntry = Heappe.Contracts.Entries.Sync.ProjectResourceInfoSyncEntry;

namespace Userorg.Adapter.Heappe.Userorg;

/// <summary>
/// Provides user and project resource management operations for the HEAppE system, including retrieval of user project
/// allocations and HEAppE endpoint version information.
/// </summary>
/// <param name="heappeIntegrationService">The integration service used to communicate with the HEAppE system. Cannot be null.</param>
/// <param name="locationService">Core location service, used to resolve a provider's current location ids. Cannot be null.</param>
/// <param name="providerService">Core provider service, used for exact-match provider name resolution. Cannot be null.</param>
/// <param name="userService">Core user service, used to resolve an identity-provider user id to the internal user id. Cannot be null.</param>
/// <param name="projectService">Core project service, used to resolve project ids to short names. Cannot be null.</param>
/// <param name="projectResourceService">Core project resource service, used to resolve resource ids to names. Cannot be null.</param>
internal sealed class UserorgService(
    IHeappeIntegrationService heappeIntegrationService,
    ILocationService locationService,
    IProviderService providerService,
    IUserService userService,
    IProjectService projectService,
    IProjectResourceService projectResourceService)
    : IUserorgService
{
    /// <summary>
    /// Asynchronously retrieves the list of project allocations associated with a specific user in the HEAppE system.
    /// </summary>
    /// <param name="idpUserId">The unique identifier of the user in the identity provider. Cannot be null.</param>
    /// <param name="heappeIdentifier">The identifier of the user in the HEAppE system. Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A result containing a list of project allocation entries for the specified user if found; otherwise, a result
    /// containing an error describing the failure.</returns>
    public async Task<Result<IList<ProjectAllocationInfoEntry>, Error>> GetUserProjectAllocationsAsync(string? idpUserId, string? heappeIdentifier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(idpUserId);
        ArgumentNullException.ThrowIfNull(heappeIdentifier);

        try
        {
            var projectAllocations = await heappeIntegrationService.GetProjectAllocationsForUserAsync(idpUserId, heappeIdentifier, ct);

            return projectAllocations.Any()
                ? Result<IList<ProjectAllocationInfoEntry>, Error>.Success(projectAllocations.Select(s => s.ConvertToModel()).ToList(), State.Ok)
                : Result<IList<ProjectAllocationInfoEntry>, Error>.Failure(Errors.FailedToRetrieveProjectsForUserWithHeappeIdentifier(idpUserId, heappeIdentifier), State.NotFound);
        }
        catch (Exception ex)
        {
            return Result<IList<ProjectAllocationInfoEntry>, Error>.Failure(Errors.UnexpectedErrorRetrievingProjectAllocations(idpUserId, heappeIdentifier, ex.Message), State.CriticalError);
        }
    }

    /// <summary>
    /// Asynchronously retrieves the resource allocation information for a specified resource within a given project.
    /// </summary>
    /// <param name="projectShortName">The short name identifier of the project. Cannot be null, empty, or consist only of white-space characters.</param>
    /// <param name="resourceName">The name of the resource for which allocation information is requested. Cannot be null, empty, or consist only
    /// of white-space characters.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a Result object that holds the
    /// project resource allocation information if found; otherwise, an error describing the failure.</returns>
    public async Task<Result<ProjectResourceInfoEntry, Error>> GetProjectResourceAllocationAsync(string projectShortName, string resourceName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectShortName))
        {
            return Result<ProjectResourceInfoEntry, Error>.Failure(CommonErrors.InvalidArgument(nameof(projectShortName)));
        }

        if (string.IsNullOrWhiteSpace(projectShortName))
        {
            return Result<ProjectResourceInfoEntry, Error>.Failure(CommonErrors.InvalidArgument(nameof(resourceName)));
        }

        try
        {
            var shortName = ProjectShortName.Create(projectShortName);
            var projectResourceAllocation = await heappeIntegrationService.GetProjectResourceAllocationAsync(shortName, resourceName, ct);

            return projectResourceAllocation is not null
                ? Result<ProjectResourceInfoEntry, Error>.Success(projectResourceAllocation.ConvertToModel(), State.Ok)
                : Result<ProjectResourceInfoEntry, Error>.Failure(Errors.FailedToRetrieveProjectAllocationWithResources(shortName, resourceName), State.NotFound);
        }
        catch (Exception ex)
        {
            return Result<ProjectResourceInfoEntry, Error>.Failure(Errors.UnexpectedErrorRetrievingProjectResourceAllocation(projectShortName, resourceName, ex.Message), State.CriticalError);
        }
    }

    /// <summary>
    /// Asynchronously retrieves the allocation details and associated location information for a specified resource
    /// within a project.
    /// </summary>
    /// <remarks>Returns a failure result if either the project short name or resource name is invalid. May
    /// return a failure result with an error if the allocation cannot be retrieved or if an unexpected error occurs
    /// during the operation.</remarks>
    /// <param name="projectShortName">The short name of the project for which resource allocation information is requested. Cannot be null or
    /// whitespace.</param>
    /// <param name="resourceName">The name of the resource whose allocation details are to be retrieved. Cannot be null or whitespace.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a success value with the project
    /// resource allocation and location information if found; otherwise, a failure result with an error describing the
    /// reason.</returns>
    public async Task<Result<ProjectResourceInfoSyncEntry, Error>> GetProjectResourceAllocationWithLocationsAsync(string projectShortName, string resourceName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectShortName))
        {
            return Result<ProjectResourceInfoSyncEntry, Error>.Failure(CommonErrors.InvalidArgument(nameof(projectShortName)));
        }

        if (string.IsNullOrWhiteSpace(projectShortName))
        {
            return Result<ProjectResourceInfoSyncEntry, Error>.Failure(CommonErrors.InvalidArgument(nameof(resourceName)));
        }

        try
        {
            var shortName = ProjectShortName.Create(projectShortName);
            var projectResourceAllocation = await heappeIntegrationService.GetProjectResourceAllocationAsync(shortName, resourceName, ct);

            return projectResourceAllocation is not null
                ? Result<ProjectResourceInfoSyncEntry, Error>.Success(projectResourceAllocation.ConvertToSyncModel(), State.Ok)
                : Result<ProjectResourceInfoSyncEntry, Error>.Failure(Errors.FailedToRetrieveProjectAllocationWithResources(shortName, resourceName), State.NotFound);
        }
        catch (Exception ex)
        {
            return Result<ProjectResourceInfoSyncEntry, Error>.Failure(Errors.UnexpectedErrorRetrievingProjectResourceAllocation(projectShortName, resourceName, ex.Message), State.CriticalError);
        }
    }

    public async Task<Result<HashSet<ProviderLocationInfo>, Error>> GetValidLocationNames(
        string projectShortName,
        string resourceName,
        IEnumerable<int> locationTypeIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var locationTypes = locationTypeIds.Select(id => id.AsLocationType());
            var locationNamesResult = await heappeIntegrationService.GetValidLocationNamesByIds(
                projectShortName,
                resourceName,
                locationTypes,
                cancellationToken);

            // Propagate result from heappeIntegrationService - it will be NotFound if project/resource doesn't exist
            if (!locationNamesResult.IsSuccess)
            {
                return Result<HashSet<ProviderLocationInfo>, Error>.Failure(locationNamesResult.Error, locationNamesResult.State);
            }

            // Return success even if dictionary is empty - empty means no locations found, which is valid
            return Result<HashSet<ProviderLocationInfo>, Error>.Success(locationNamesResult.Value, State.Ok);
        }
        catch (Exception ex)
        {
            var error = new ErrorBuilder()
                .WithDescription($"Failed to retrieve location names for project '{projectShortName}' and resource '{resourceName}': {ex.Message}")
                .WithSeverity(ErrorSeverity.Error)
                .Build();
            return Result<HashSet<ProviderLocationInfo>, Error>.Failure(error, State.CriticalError);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Guid, Error>> ResolveProviderIdByNameAsync(string providerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return Result<Guid, Error>.Failure(CommonErrors.InvalidArgument(nameof(providerName)));
        }

        try
        {
            var providers = await providerService.GetProvidersAsync(countryCode: null, onlyPublic: false, ct);
            var matches = providers
                .Where(p => p.Id.HasValue && string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matches.Count switch
            {
                1 => Result<Guid, Error>.Success(matches[0].Id!.Value, State.Ok),
                0 => Result<Guid, Error>.Failure(Errors.ProviderNotFoundByName(providerName), State.NotFound),
                _ => Result<Guid, Error>.Failure(Errors.AmbiguousProviderName(providerName), State.CriticalError),
            };
        }
        catch (Exception ex)
        {
            return Result<Guid, Error>.Failure(Errors.UnexpectedErrorResolvingProviderName(providerName, ex.Message), State.CriticalError);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IList<Guid>, Error>> GetLocationIdsForProviderAsync(Guid providerId, CancellationToken ct)
    {
        try
        {
            var locationIds = await locationService.GetLocationIdsForProviderIdAsync(providerId, ct);
            return Result<IList<Guid>, Error>.Success(locationIds, State.Ok);
        }
        catch (Exception ex)
        {
            return Result<IList<Guid>, Error>.Failure(Errors.UnexpectedErrorRetrievingLocationIdsForProvider(providerId, ex.Message), State.CriticalError);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IList<ProviderLocationEntry>, Error>> GetProviderLocationsAsync(CancellationToken ct)
    {
        try
        {
            var locations = await locationService.GetLocationsAsync(providerName: null, locationTypeId: null, onlyPublic: false, ct);

            var entries = locations
                .Where(l => l.Id.HasValue && l.Provider?.Id is not null)
                .Select(l => new ProviderLocationEntry(l.Id!.Value, l.Name ?? string.Empty, l.Provider!.Id!.Value, l.Provider.Name ?? string.Empty))
                .ToList();

            return Result<IList<ProviderLocationEntry>, Error>.Success(entries, State.Ok);
        }
        catch (Exception ex)
        {
            return Result<IList<ProviderLocationEntry>, Error>.Failure(Errors.UnexpectedErrorRetrievingProviderLocations(ex.Message), State.CriticalError);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Guid, Error>> ResolveUserIdByIdentityProviderIdAsync(string identityProviderUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identityProviderUserId))
        {
            return Result<Guid, Error>.Failure(CommonErrors.InvalidArgument(nameof(identityProviderUserId)));
        }

        try
        {
            var user = await userService.GetUserBySidAsync(identityProviderUserId, ct);
            return user is not null
                ? Result<Guid, Error>.Success(user.Id, State.Ok)
                : Result<Guid, Error>.Failure(Errors.UserNotFoundByIdentityProviderId(identityProviderUserId), State.NotFound);
        }
        catch (Exception ex)
        {
            return Result<Guid, Error>.Failure(Errors.UnexpectedErrorResolvingUser(identityProviderUserId, ex.Message), State.CriticalError);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IList<ProjectResourceNameEntry>, Error>> ResolveProjectResourceNamesAsync(
        IReadOnlyCollection<ProjectResourceIdEntry> projectResourceIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectResourceIds);

        if (projectResourceIds.Count == 0)
        {
            return Result<IList<ProjectResourceNameEntry>, Error>.Success([], State.Ok);
        }

        try
        {
            var projects = await projectService.GetProjectsAsync(null, null, null, null, null, providerId: null, ct);
            var shortNamesByProjectId = projects
                .Where(p => p.Id.HasValue && p.ShortName is not null)
                .ToDictionary(p => p.Id!.Value.Value, p => p.ShortName!);

            var resources = await projectResourceService.GetProjectResourcesAsync(null, (ProjectId?)null, null, null, null, null, ct);
            var namesByResourceId = resources
                .Where(r => r.Id.HasValue && r.Name is not null)
                .ToDictionary(r => r.Id!.Value, r => r.Name!);

            var entries = projectResourceIds
                .Where(ids => shortNamesByProjectId.ContainsKey(ids.ProjectId) && namesByResourceId.ContainsKey(ids.ProjectResourceId))
                .Select(ids => new ProjectResourceNameEntry(
                    ids.ProjectId,
                    ids.ProjectResourceId,
                    shortNamesByProjectId[ids.ProjectId],
                    namesByResourceId[ids.ProjectResourceId]))
                .ToList();

            return Result<IList<ProjectResourceNameEntry>, Error>.Success(entries, State.Ok);
        }
        catch (Exception ex)
        {
            return Result<IList<ProjectResourceNameEntry>, Error>.Failure(Errors.UnexpectedErrorResolvingProjectResourceNames(ex.Message), State.CriticalError);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IList<ProjectResourceNameEntry>, Error>> ResolveProjectResourceIdsAsync(
        IReadOnlyCollection<ProjectResourceNamePair> pairs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            return Result<IList<ProjectResourceNameEntry>, Error>.Success([], State.Ok);
        }

        try
        {
            var projects = await projectService.GetProjectsAsync(null, null, null, null, null, providerId: null, ct);
            var projectsByShortName = BuildUnambiguousLookup(
                projects.Where(p => p.Id.HasValue && p.ShortName is not null),
                p => p.ShortName!);

            var resources = await projectResourceService.GetProjectResourcesAsync(null, (ProjectId?)null, null, null, null, null, ct);
            var resourcesByName = BuildUnambiguousLookup(
                resources.Where(r => r.Id.HasValue && r.Name is not null),
                r => r.Name!);

            var entries = pairs
                .Where(pair => projectsByShortName.ContainsKey(pair.ProjectShortName) && resourcesByName.ContainsKey(pair.ResourceName))
                .Select(pair => (Project: projectsByShortName[pair.ProjectShortName], Resource: resourcesByName[pair.ResourceName]))
                .Where(match => match.Project.AssignedProjectResourceIds?.Contains(match.Resource.Id!.Value) == true)
                .Select(match => new ProjectResourceNameEntry(
                    match.Project.Id!.Value.Value,
                    match.Resource.Id!.Value,
                    match.Project.ShortName!,
                    match.Resource.Name!))
                .ToList();

            return Result<IList<ProjectResourceNameEntry>, Error>.Success(entries, State.Ok);
        }
        catch (Exception ex)
        {
            return Result<IList<ProjectResourceNameEntry>, Error>.Failure(Errors.UnexpectedErrorResolvingProjectResourceIds(ex.Message), State.CriticalError);
        }
    }

    /// <summary>
    /// Indexes a set by a non-unique display name, dropping every name that matches more than one row so an
    /// ambiguous name resolves to nothing rather than to an arbitrary winner. Keys match case-insensitively;
    /// the matched row is returned so callers read Core's spelling, not the one they looked up with.
    /// </summary>
    private static Dictionary<string, TSource> BuildUnambiguousLookup<TSource>(
        IEnumerable<TSource> source,
        Func<TSource, string> nameSelector)
        => source
            .GroupBy(nameSelector, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
}