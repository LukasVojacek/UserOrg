
using Core.Domain.Exceptions;

using FluentValidation;
using FluentValidation.Results;

using Forge.Core.Features.Role.Services;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ValueObjects;

// Disambiguate from main's new Heappe.Contracts.IHeappeConnectionService: legacy ProjectService uses the
// Application-layer service (Result-returning, GetListAvailableClusters). See ADR 0033 (merge port-later list).
using IHeappeConnectionService = UserOrganizationManagement.Application.UserOrg.IServices.IHeappeConnectionService;

namespace UserOrganizationManagement.Application.UserOrg.Services;

/// <summary>
/// Constructor
/// </summary>
/// <param name="userOrgUnitOfWork">UserOrg UnitOfWork</param>
public class ProjectService(IUserOrgUnitOfWork userOrgUnitOfWork, IHttpClientFactory httpClientFactory, IFeatureManager featureManager, HybridCache cache, IHeappeConnectionService heappeService, ILogger<ProjectService> logger, IRoleListService roleListService) : IProjectService
{
    private static readonly HybridCacheEntryOptions _templatesInfoCacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(5)
    };

    // noCache: bypass read AND write on both local and distributed cache so the factory always runs and its
    // result is never stored. A pure pass-through that neither reads, refreshes, nor invalidates the shared
    // TemplatesInfo entry — concurrent normal callers keep being served the existing cached snapshot.
    private static readonly HybridCacheEntryOptions _templatesInfoBypassCacheOptions = new()
    {
        Flags = HybridCacheEntryFlags.DisableLocalCacheRead
              | HybridCacheEntryFlags.DisableLocalCacheWrite
              | HybridCacheEntryFlags.DisableDistributedCacheRead
              | HybridCacheEntryFlags.DisableDistributedCacheWrite
    };

    /// <summary>
    /// Internal control-flow signal thrown out of the TemplatesInfo HybridCache factory when the build is
    /// degraded or empty, so the snapshot is returned to the caller but never written to the cache. Not an
    /// error and never surfaces to API callers — caught in <see cref="ProjectTemplateInfo"/>.
    /// </summary>
    private sealed class DegradedTemplatesInfoSignal(List<TemplateInfoResponse> responses, bool allDegraded, IReadOnlyList<HeappeFailureInfo> failures) : Exception
    {
        public List<TemplateInfoResponse> Responses { get; } = responses;

        /// <summary>
        /// True when EVERY HEAppE cluster query in the build failed (no instance returned usable data).
        /// Distinguishes a total outage — surfaced to API callers as 503 — from a partial degradation,
        /// where the available snapshot is still returned with 200.
        /// </summary>
        public bool AllDegraded { get; } = allDegraded;

        /// <summary>
        /// HEAppE instances that did not return results, each with a sanitized descriptor and a cause code.
        /// Carried on the signal so every single-flight-joined caller observes the same list; on partial
        /// degradation the descriptors surface as a header, on total degradation the whole list is the 503 body.
        /// </summary>
        public IReadOnlyList<HeappeFailureInfo> Failures { get; } = failures;
    }

    /// <summary>
    /// Retrieves projects filtered by various criteria, with optional provider scoping for ProviderAdmin callers.
    /// </summary>
    /// <param name="shortName">Optional project short name filter.</param>
    /// <param name="contactEmail">Optional contact email filter.</param>
    /// <param name="domainId">Optional domain ID filter.</param>
    /// <param name="startDate">Optional start date filter (inclusive).</param>
    /// <param name="endDate">Optional end date filter (inclusive).</param>
    /// <param name="providerId">Optional provider ID for scoping results to a provider's assigned resources.
    /// When <c>null</c>, no provider filter is applied (platform admin sees all projects).
    /// When set to a provider GUID, the result is the union of projects whose resource-assignment chain resolves to
    /// that provider and the caller's own projects (see <paramref name="callerIdpUserId"/>).
    /// Soft-deleted resource assignments and locations are excluded from the scope chain.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="callerIdpUserId">The calling ProviderAdmin's identity-provider user id. When
    /// <paramref name="providerId"/> is set, a project the caller is assigned to or created is included even
    /// outside the provider's resource chain. Ignored when <paramref name="providerId"/> is <c>null</c>.</param>
    /// <returns>A list of projects matching the filters. Returns empty list if no matches, or throws RecordNotFoundException
    /// if a filter was supplied and no results were found.</returns>
    public async Task<IList<ProjectModel>> GetProjectsAsync(ProjectShortName? shortName, string? contactEmail, int? domainId, DateTime? startDate, DateTime? endDate, Guid? providerId, CancellationToken ct, string? callerIdpUserId = null)
    {
        var projects = await userOrgUnitOfWork.ProjectRepository.GetAllBySpecificationsAsync(shortName, contactEmail, domainId, startDate, endDate, providerId, ct, callerIdpUserId).ConfigureAwait(true);

        if ((shortName is not null || contactEmail is not null || domainId is not null) && projects.Count == 0)
        {
            var projShortName = string.IsNullOrEmpty(shortName) ? string.Empty : $"Short-Name:{shortName.Value}";
            var emailContact = string.IsNullOrEmpty(contactEmail) ? string.Empty : $"ContactEmail:{contactEmail}";
            var domain = domainId is null ? string.Empty : $"DomainId:{domainId}";
            var start = startDate is null ? string.Empty : $"StartDate:{startDate}";
            var end = endDate is null ? string.Empty : $"EndDate:{endDate}";
            throw new RecordNotFoundException($"""Project for input parameters: {projShortName} {emailContact} {domain} {start} {end} do not exist!""");
        }

        return projects.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<List<ProjectModel>> GetProjectsAsync(ProjectShortName? projectShortName, string idpUserId, CancellationToken ct)
    {
        var projects = await userOrgUnitOfWork.ProjectRepository.GetProjectsContainsUserAsync(projectShortName, idpUserId, ct);

        return projects is null
            ? throw new RecordNotFoundException($"""Projects for IdPUserId {idpUserId} contains Short-Name:"{projectShortName}" does not exist!""")
            : await Task.FromResult(projects.Select(x => x.ConvertToModel()).ToList());
    }
    public async Task<ProjectModel> GetProjectAsync(ProjectShortName projectShortName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(projectShortName.Value);

        var project = await userOrgUnitOfWork.ProjectRepository.GetProjectByShortNameWithCreatedUsersAsync(projectShortName, ct);

        return project is null
            ? throw new RecordNotFoundException($"""Project contains Short-Name:"{projectShortName}" does not exist!""")
            : await Task.FromResult(project.ConvertToModel());
    }

    public async Task<Guid> InsertProjectAsync(ProjectCreate projectCreate, Guid createdByUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectCreate);

        var domain = await userOrgUnitOfWork.ProjectDomainTypeRepository.GetByIdAsync(projectCreate.DomainId!.Value, ct)
            ?? throw new RecordNotFoundException($"""Project Domain type with id:"{projectCreate.DomainId}" does not exist!""");
        var createdByUser = await userOrgUnitOfWork.UserRepository.GetByIdAsync(createdByUserId, ct)
            ?? throw new RecordNotFoundException($"""User with Sid:"{createdByUserId}" does not exist!""");
        var createdProject = projectCreate.ConvertToEntity(domain, createdByUser);

        await userOrgUnitOfWork.ProjectRepository.InsertAsync(createdProject, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(createdProject.ShortName.Value), ct);
        return createdProject.Id;
    }

    public async Task UpdateProjectAsync(ProjectUpdate projectUpdate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectUpdate);

        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectUpdate.ShortName, ct)
            ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectUpdate.ShortName.Value}" does not exist!""");
        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);

        var domain = project.ProjectDomainType;

        if (project.ProjectDomainType.Id != projectUpdate.DomainId)
        {
            domain = await userOrgUnitOfWork.ProjectDomainTypeRepository.GetByIdAsync(projectUpdate.DomainId!.Value, ct);
            if (domain is null)
            {
                throw new RecordNotFoundException($"""Project Domain type with Id:"{projectUpdate.DomainId}" does not exist!""");
            }
        }

        UpdateEntity(project, projectUpdate, domain);
        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(projectUpdate.ShortName.Value), ct);
    }

    public async Task DeleteProjectAsync(ProjectShortName projectShortName, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct)
            ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectShortName}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);

        foreach (var permission in project.UserPermissions)
        {
            permission.IsDeleted = true;
            if (permission?.User?.Id is { } userId)
            {
                await cache.RemoveByTagAsync($"UserInfoModel_{userId}", ct);
            }
        }

        await userOrgUnitOfWork.ProjectRepository.DeleteAsync(project.Id, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(projectShortName.Value), ct);
    }

    public async Task EditAssignedUserSystemRole(ProjectShortName projectShortName, string idpUserId, string idpRequesterUserId, int systemRoleId, bool isAdmin, CancellationToken ct)
    {

        var projMemberRoleRequest = await roleListService.ListRoles(ct);
        if (!projMemberRoleRequest.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }

        var user = await userOrgUnitOfWork.UserRepository.GetBySidWithProjectsAsync(idpUserId, ct);
        UserNotFoundException.ThrowIfNull(user);

        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct);
        ProjectNotFoundException.ThrowIfNull(project);

        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);

        var requesterRole = await GetProjectRole(projectShortName, idpRequesterUserId, ct)
                ;

        var targetRole = projMemberRoleRequest.Value.FirstOrDefault(x => x.Id == systemRoleId);
        ArgumentNullException.ThrowIfNull(targetRole);

        var projectMemberRole = projMemberRoleRequest.Value.FirstOrDefault(x => x.Name.Equals(RoleInfoConstants.ProjectMemberRoleName, StringComparison.OrdinalIgnoreCase));

        var userProjectRole = await GetProjectRole(projectShortName, idpRequesterUserId, ct);

        var ownerRoleRequest = projMemberRoleRequest.Value.FirstOrDefault(x => x.Name == RoleInfoConstants.ProjectOwnerRoleName);
        ArgumentNullException.ThrowIfNull(ownerRoleRequest);

        var projectOwner = await userOrgUnitOfWork.UserPermissionRepository.GetPermissionForProjectBySystemRoleAsync(project.Id, ownerRoleRequest.Id, ct)
            ;
        if (projectOwner is not null && projectOwner.SystemRoleId == systemRoleId && isAdmin)
        {
            throw new AssignDuplicateProjectOwnerRoleException("You can not assign more project owners for project!");
        }

        if (!requesterRole?.IsHigherLevelThan(targetRole) ?? true)
        {
            throw new AssignRoleOfSameOrHigherLevelException();
        }

        if (projectMemberRole?.IsHigherLevelThan(targetRole) ?? false)
        {
            throw new AssignRoleOfSameOrHigherLevelException();
        }

        var permissionEntry = project.UserPermissions.FirstOrDefault(x => x.User.Id == user.Id);
        if (projectMemberRole is null || permissionEntry is null)
        {
            var userPermission = new UserPermission
            {
                CreatedAt = DateTimeOffset.UtcNow.DateTime,
                Project = project,
                User = user
            };
            project.UserPermissions.Add(userPermission);
            await userOrgUnitOfWork.UserPermissionRepository.InsertAsync(userPermission, ct);
        }

        permissionEntry ??= project.UserPermissions.First(x => x.User.Id == user.Id);
        permissionEntry.SystemRoleId = (int)targetRole?.Id!;

        await userOrgUnitOfWork.SaveAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
        await cache.RemoveByTagAsync($"UserInfoModel_{user.Id}", ct);

    }
    public async Task EditAssignedUserSystemRole(ProjectShortName projectShortName, string idpUserId, string idpRequesterUserId, string systemRoleName, CancellationToken ct)
    {
        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }

        var roleFromRoleId = roles.Value.FirstOrDefault(x => x.Name.Equals(systemRoleName, StringComparison.OrdinalIgnoreCase));
        ArgumentNullException.ThrowIfNull(roleFromRoleId);

        var user = await userOrgUnitOfWork.UserRepository.GetBySidWithProjectsAsync(idpUserId, ct);
        UserNotFoundException.ThrowIfNull(user);

        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct);
        ProjectNotFoundException.ThrowIfNull(project);

        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);

        var requesterRole = await GetProjectRole(projectShortName, idpRequesterUserId, ct);
        ArgumentNullException.ThrowIfNull(requesterRole);

        var projectMemberRole = roles.Value.FirstOrDefault(x => x.Name.Equals(RoleInfoConstants.ProjectMemberRoleName, StringComparison.OrdinalIgnoreCase));
        ArgumentNullException.ThrowIfNull(projectMemberRole);

        if (!requesterRole.IsHigherLevelThan(roleFromRoleId))
        {
            throw new AssignRoleOfSameOrHigherLevelException();
        }

        if (projectMemberRole.IsHigherLevelThan(roleFromRoleId))
        {
            throw new AssignRoleOfSameOrHigherLevelException();
        }

        project.UserPermissions.First(x => x.User.Id == user.Id)
            .SystemRoleId = roleFromRoleId.Id;

        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync($"UserInfoModel_{user.Id}", ct);

    }

    public async Task AssignedProjectResourceToProjectAsync(ProjectShortName projectShortName, string projectResourceName, Guid? providerId, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct)
          ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectShortName}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);

        var projectResource = await userOrgUnitOfWork.ProjectResourceRepository.GetByNameWithIncludesAsync(projectResourceName, ct)
            ?? throw new RecordNotFoundException($"""Project Resource with id:"{projectResourceName}" does not exist!""");

        if (providerId.HasValue && !projectResource.AssignedLocations.Any(x => x.Location.Provider.Id == providerId))
        {
            throw new ValidationException(new ValidationFailure[1] { new(nameof(projectResource), "You can assign only project resources from yours Provider") });
        }

        var assignedProjectResourceToProject = project.AssignedProjectResources.FirstOrDefault(f => f.ProjectResource == projectResource);
        if (assignedProjectResourceToProject is not null)
        {
            if (!assignedProjectResourceToProject.IsDeleted)
            {
                throw new RecordAlreadyExistException($"""Project Resource "{projectResourceName}" is already associated with Project "{projectShortName}"!""");
            }

            assignedProjectResourceToProject.IsDeleted = false;
            await userOrgUnitOfWork.SaveAsync(ct);
            await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(projectShortName.Value), ct);
            await EvictHeappeClusterCacheAsync(projectShortName, ct);
            return;
        }

        if (project.AssignedProjectResources.Any(x => !x.IsDeleted && x.ProjectResource.AssignedLocations.Any(l => !l.IsDeleted && !l.Location.IsShared)))
        {
            throw new ProjectAlreadyHasAssignedNonSharedResourceException();
        }

        // No repository exists for AssignedProjectResourceToProject, and its composite key
        // (ProjectId, ProjectResourceId) is never store-generated, so it doesn't share the #280
        // Added/Modified ambiguity that the UserPermission inserts elsewhere in this class guard
        // against — no InsertAsync pairing here.
        project.AssignedProjectResources.Add(new AssignedProjectResourceToProject()
        {
            Project = project,
            ProjectResource = projectResource
        });
        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(projectShortName.Value), ct);
        await EvictHeappeClusterCacheAsync(projectShortName, ct);
    }

    /// <summary>
    /// Evicts the shared per-instance HEAppE cluster cache (layer 2) for every instance the project is
    /// linked to. Called when a resource gets attached to the project: that is the moment the HEAppE-side
    /// allocation typically appears, and a catalog snapshot fetched moments earlier would not contain it —
    /// with nothing but the 5-minute TTL (or a manual sync) to heal it. Unassign does not need this: a
    /// stale catalog cannot hide the absence of data. See docs/components/core/features/caching.md.
    /// </summary>
    private async Task EvictHeappeClusterCacheAsync(ProjectShortName projectShortName, CancellationToken ct)
    {
        var links = await userOrgUnitOfWork.ProjectRepository.GetHeappeLinks(projectShortName, ct);
        foreach (var identifier in links.Where(l => l.HEAppEApiIdentifier is not null).Select(l => l.HEAppEApiIdentifier!.Value).Distinct())
        {
            await cache.RemoveByTagAsync(CacheTags.HeappeClusters(identifier), ct);
        }
    }

    public async Task UnAssignedProjectResourceToProjectAsync(ProjectShortName projectShortName, string projectResourceName, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct)
            ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectShortName}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);

        var projectResource = await userOrgUnitOfWork.ProjectResourceRepository.GetByNameWithIncludesAsync(projectResourceName, ct)
            ?? throw new RecordNotFoundException($"""Project resource with id:"{projectResourceName}" does not exist!""");
        var assignedProjectResourceToProject = project.AssignedProjectResources.FirstOrDefault(f => !f.IsDeleted && f.ProjectResource.Id == projectResource.Id)
            ?? throw new RecordNotFoundException($"""Project Resource "{projectResourceName}" association with Project "{projectShortName}" was already removed!""");

        assignedProjectResourceToProject.IsDeleted = true;
        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(projectShortName.Value), ct);
    }

    public async Task AssignedUserToProjectWithRoleAsync(ProjectShortName projectShortName, string idpUserId, Guid assignedByUserId, string roleName, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct)
            ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectShortName}" does not exist!""");
        var user = await userOrgUnitOfWork.UserRepository.GetBySidWithProjectsAsync(idpUserId, ct)
            ?? throw new RecordNotFoundException($"""User with idpUserId:"{idpUserId}" does not exist!""");
        var assignedByUser = await userOrgUnitOfWork.UserRepository.GetByIdAsync(assignedByUserId, ct)
            ?? throw new RecordNotFoundException($"""User with Sid:"{assignedByUserId}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);
        var assignedUserToProject = project.AssignedUsers.FirstOrDefault(f => f.User == user);

        var projMemberRoleRequest = await roleListService.ListRoles(ct);
        if (!projMemberRoleRequest.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }
        var role = projMemberRoleRequest.Value.FirstOrDefault(x => x.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));

        if (role == null)
        {
            throw new RecordNotFoundException($"""Role with Name:"{roleName}" does not exist!""");
        }

        var existingPermission = project.UserPermissions.FirstOrDefault(x => x.User.IdentityProviderUserId == idpUserId);
        if (assignedUserToProject is not null)
        {
            if (!assignedUserToProject.IsDeleted)
            {
                throw new RecordAlreadyExistException($"""User "{idpUserId}" is already associated with Project "{projectShortName}"!""");
            }

            assignedUserToProject.IsDeleted = false;
            if (existingPermission is not null)
            {
                existingPermission.SystemRoleId = role.Id;
                existingPermission.IsDeleted = false;
            }

            await userOrgUnitOfWork.SaveAsync(ct);
            await cache.RemoveByTagAsync($"UserInfoModel_{user.Id}", ct);
            return;
        }

        // No repository exists for AssignedUserToProject, and its composite key (ProjectId, UserId)
        // is never store-generated, so it doesn't share the #280 Added/Modified ambiguity that the
        // UserPermission insert below guards against — no InsertAsync pairing here.
        project.AssignedUsers.Add(new AssignedUserToProject()
        {
            Project = project,
            User = user,
            AssignedByUser = assignedByUser
        });

        if (existingPermission is not null)
        {
            existingPermission.SystemRoleId = role.Id;
        }
        else
        {
            var userPermission = new UserPermission
            {
                Project = project,
                User = user,
                SystemRoleId = role.Id
            };
            project.UserPermissions.Add(userPermission);
            await userOrgUnitOfWork.UserPermissionRepository.InsertAsync(userPermission, ct);
        }

        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync($"UserInfoModel_{user.Id}", ct);

    }

    public async Task UnAssignedUserFromProjectAsync(ProjectShortName projectShortName, string idpUserId, Guid unassignedByUserId, CancellationToken ct)
    {
        const int projectOwnerSystemRoleId = 4;
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct)
            ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectShortName}" does not exist!""");
        var user = await userOrgUnitOfWork.UserRepository.GetBySidWithProjectsAsync(idpUserId, ct)
            ?? throw new RecordNotFoundException($"""User with idpUserId:"{idpUserId}" does not exist!""");
        var unassignedByUser = await userOrgUnitOfWork.UserRepository.GetByIdAsync(unassignedByUserId, ct)
            ?? throw new RecordNotFoundException($"""User with Sid:"{unassignedByUserId}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(project.Mode);
        var assignedUserToProject = project.AssignedUsers.FirstOrDefault(f => !f.IsDeleted && f.User == user)
            ?? throw new RecordNotFoundException($"""User "{idpUserId}" association with Project "{projectShortName}" was already removed!""");

        var userPermission = project.UserPermissions.FirstOrDefault(x => x.User.IdentityProviderUserId == idpUserId)
             ?? throw new RecordNotFoundException($"""UserPermission for "{idpUserId}" association with Project "{projectShortName}" was not found!""");
        var unassignedByRoleId = project.UserPermissions.First(x => x.User.Id == unassignedByUser.Id).SystemRoleId;

        if (unassignedByRoleId is <= 1 or > 5)
        {
            throw new ValidationException(new ValidationFailure[1]
            {
                new(nameof(userPermission), $"Insufficient permission to remove user from project.")
            });
        }

        if (userPermission.SystemRoleId == projectOwnerSystemRoleId)
        {
            throw new ValidationException(new ValidationFailure[1]
            {
                new(nameof(userPermission), $"User with roleId= {userPermission.SystemRoleId} can not be unassigned from {projectShortName}")
            });
        }

        assignedUserToProject.AssignedByUser = unassignedByUser;
        assignedUserToProject.IsDeleted = true;

        userPermission.IsDeleted = true;
        await userOrgUnitOfWork.SaveAsync(ct);
        await cache.RemoveByTagAsync($"UserInfoModel_{user.Id}", ct);

    }

    public async Task AssignedProjectTagToProjectAsync(ProjectShortName projectShortName, Guid projectTagId, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct)
            ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectShortName}" does not exist!""");
        var projectTag = await userOrgUnitOfWork.ProjectTagRepository.GetByIdAsync(projectTagId, ct)
            ?? throw new RecordNotFoundException($"""ProjectTag with id:"{projectTagId}" does not exist!""");

        var associatedProjectTag = project.ProjectTags.FirstOrDefault(f => f.Id == projectTag.Id);
        if (associatedProjectTag is not null)
        {
            throw new RecordAlreadyExistException($"""ProjectTag "{projectTagId}" is already associated with Project "{projectShortName}"!""");
        }

        project.ProjectTags.Add(projectTag);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    public async Task UnAssignedProjectTagToProjectAsync(ProjectShortName projectShortName, Guid projectTagId, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct)
            ?? throw new RecordNotFoundException($"""Project with Short-Name:"{projectShortName}" does not exist!""");
        var projectTag = await userOrgUnitOfWork.ProjectTagRepository.GetByIdAsync(projectTagId, ct)
            ?? throw new RecordNotFoundException($"""ProjectTag with id:"{projectTagId}" does not exist!""");

        _ = project.ProjectTags.FirstOrDefault(f => f.Id == projectTag.Id)
            ?? throw new RecordAlreadyExistException($"""ProjectTag "{projectTagId}" is already unassociated with Project "{projectShortName}"!""");

        project.ProjectTags.Remove(projectTag);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    public async Task<IList<ProjectUserModel>> GetProjectUsersWithRoles(ProjectShortName projectShortName, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetProjectByShortNameWithCreatedUsersAsync(projectShortName, ct)
            ?? throw new RecordNotFoundException($"""Project contains Short-Name:"{projectShortName}" does not exist!""");

        var validUsers = project.AssignedUsers
            .Where(u => !u.IsDeleted)
            .Select(u => u.User.Id)
            .ToList();

        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }

        var result = project.UserPermissions
            .Where(x => !x.IsDeleted && x.User is not null && validUsers.Any(u => u == x.User.Id))
            .Select(x => new ProjectUserModel(x.User.IdentityProviderUserId, x.User.Email, roles.Value.FirstOrDefault(r => r.Id == x.SystemRoleId)?.Name!))
            .ToList();

        return result;
    }
    public async Task<RoleInfo?> GetProjectRole(ProjectShortName projectShortName, string idpUserId, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetProjectByShortNameWithCreatedUsersAsync(projectShortName, ct);
        var roleId = project?.UserPermissions.FirstOrDefault(x => x.User?.IdentityProviderUserId.Equals(idpUserId, StringComparison.OrdinalIgnoreCase) == true && !x.IsDeleted)?.SystemRoleId;
        if (roleId is null)
        {
            logger.LogInformation("""User with IdPUserId:"{idpUserId}" does not have a role assigned in Project "{projectShortName}"!""", idpUserId, projectShortName);
            return null;
        }
        var roles = await roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }
        var role = roles.Value.FirstOrDefault(x => x.Id == roleId.Value);
        return role is null
            ? roles.Value.GetLowestLevelRole("project")
            : role;
    }

    public async Task<IList<Project>> GetProjectsForProvider(Guid providerId, CancellationToken ct) => await userOrgUnitOfWork.ProjectRepository.GetProjectsForProvider(providerId, ct)
          ;

    private static void UpdateEntity(Project project, ProjectUpdate projectUpdate, ProjectDomainType domain)
    {
        //ShortName instead of Id
        project.ShortName = projectUpdate.ShortName;
        project.Name = projectUpdate.Name;
        project.Description = projectUpdate.Description;
        project.Email = projectUpdate.ContactEmail;
        project.StartDate = projectUpdate.StartDate;
        project.EndDate = projectUpdate.EndDate;
        project.ProjectDomainType = domain;
    }

    public async Task<IList<IProjectHEAppELinks>> GetProjectDetailAsync(ProjectShortName projectShortName, CancellationToken ct)
    {
        var data = await userOrgUnitOfWork.ProjectRepository.GetHeappeLinks(projectShortName, ct);
        return data;
    }

    public async Task<ExtendedProjectModel> GetExtendedProjectInfoAsync(ProjectShortName projectShortName, CancellationToken ct)
    {
        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameWithDetailsAsync(projectShortName, ct);
        return project.ConvertToExtendedModel();

    }

    public async Task<TemplatesInfoResult> ProjectTemplateInfo(ProjectShortName projectShortName, string bearerToken, bool noCache, CancellationToken ct)
    {
        try
        {
            // A cache hit runs no factory, so none of the diagnostic logging inside the build fires. Track
            // whether this call actually built the snapshot, so an empty response can be told apart from an
            // empty snapshot served out of the cache without any log line explaining it.
            var builtByThisCall = false;

            var responses = await cache.GetOrCreateAsync(
                CacheTags.TemplatesInfo(projectShortName.Value),
                async token =>
                {
                    builtByThisCall = true;
                    var (built, isDegraded, allDegraded, failures) = await BuildProjectTemplateInfoAsync(projectShortName, bearerToken, noCache, token);

                    // A degraded build (HEAppE down, unreachable, or mid-hydration) must never reach the cache.
                    // Throwing out of the factory keeps HybridCache's single-flight (all joined callers observe
                    // this signal) while writing nothing, so no degraded entry ever exists for another request to
                    // read. See docs/components/core/features/caching.md.
                    //
                    // An EMPTY build is not cached either: the login-triggered warm-up can snapshot a project
                    // mid-propagation (links/allocations not yet visible) and a cached [] would pin that
                    // transient emptiness for the full TTL with nothing but the sync invalidating layer 2.
                    // Rebuilding an empty snapshot is cheap — the cluster catalog still comes from the layer-2
                    // cache — so emptiness always re-evaluates live and heals as soon as the data appears.
                    if (isDegraded || built.Count == 0)
                        throw new DegradedTemplatesInfoSignal(built, allDegraded, failures);

                    return built;
                },
                noCache ? _templatesInfoBypassCacheOptions : _templatesInfoCacheOptions,
                tags: [CacheTags.TemplatesInfo(projectShortName.Value), CacheTags.KillSwitchGlobal],
                cancellationToken: ct);

            // An empty build never reaches this path anymore (it throws the non-caching signal below); the
            // only reachable empty here is a cache entry written before empty builds stopped being cached
            // (deploy transition, gone within one TTL). Log it so an operator investigating an empty
            // response in that window is not left with nothing at default level.
            if (responses.Count == 0)
            {
                logger.LogInformation(
                    "TemplatesInfo for project {ProjectShortName}: EMPTY result served from a stale pre-deploy cache entry; it expires within the 5-minute TTL",
                    projectShortName.Value);
            }

            logger.LogDebug(
                "TemplatesInfo for project {ProjectShortName}: returning {ResponseCount} location entr(ies). Source={Source}, noCache={NoCache}",
                projectShortName.Value, responses.Count, builtByThisCall ? "Build" : "Cache", noCache);

            // A fully-successful build: every queried instance answered, so no unavailable-instance header.
            return new TemplatesInfoResult(responses, []);
        }
        catch (DegradedTemplatesInfoSignal signal)
        {
            // ADR 0073: no wait-and-rebuild. A degraded build is returned at once and is never cached.
            // A total outage (every HEAppE instance failed) surfaces as 503 so the client can tell "HEAppE is
            // down, retry later" from "this project genuinely has no templates". A partial degradation (some
            // instances answered) returns the available snapshot with 200; the cache heals on a later call once
            // the unavailable instance recovers. Waiting cannot help under the endpoint's 60 s wall, and the
            // background warmer rides out hydration by re-firing on the next login, not by an inner loop.
            if (signal.AllDegraded)
            {
                logger.LogError(
                    "TemplatesInfo for project {ProjectShortName}: all HEAppE cluster queries failed — returning 503 Service Unavailable",
                    projectShortName.Value);
                throw new HeappeUnavailableException(
                    $"All HEAppE cluster queries failed for project '{projectShortName.Value}'; templates information is temporarily unavailable.",
                    signal.Failures);
            }

            // Information, not Warning: a legitimately empty project (no HEAppE links) takes this path on
            // every call, and the per-step build warnings already flag the anomalous empties.
            if (signal.Responses.Count == 0)
            {
                logger.LogInformation(
                    "TemplatesInfo for project {ProjectShortName}: returning an EMPTY result that was NOT cached — the next call re-evaluates live (data may still be propagating). noCache={NoCache}",
                    projectShortName.Value, noCache);
            }

            return new TemplatesInfoResult(signal.Responses, signal.Failures.Select(f => f.Instance).Distinct().ToList());
        }
        catch (ProjectNotFoundException)
        {
            // Not an unexpected error — GlobalExceptionHandler maps this to HTTP 404 for direct API
            // callers; the background cache warmer (WarmAsync) already handles it at Warning level.
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller disconnected — not an error.
            throw;
        }
        catch (Exception ec)
        {
            logger.LogError(ec, "Error while building ProjectTemplateInfo for project {ProjectShortName}", projectShortName);
            throw;
        }
    }

    private async Task<(List<TemplateInfoResponse> Responses, bool Degraded, bool AllDegraded, IReadOnlyList<HeappeFailureInfo> Failures)> BuildProjectTemplateInfoAsync(ProjectShortName projectShortName, string bearerToken, bool noCache, CancellationToken ct)
    {
        // Guard: empty ShortName originates from global/system roles (ProjectId = null in DB).
        // These are filtered in ToProjectRecords, but guard here as defense-in-depth.
        if (string.IsNullOrEmpty(projectShortName.Value))
        {
            logger.LogWarning("TemplatesInfo called with an empty ProjectShortName — returning an empty result without querying anything");
            return ([], false, false, []);
        }

        var currentDate = DateTime.Today;

        var project = await userOrgUnitOfWork.ProjectRepository.GetByShortNameAsync(projectShortName, ct);

        // Guard: project may not exist in the DB even when its ShortName is non-empty —
        // Keycloak roles can reference projects that were deleted from userorg (orphaned roles).
        // Skip silently during cache warm-up; direct API callers get 404 via ProjectNotFoundException.
        if (project is null)
        {
            logger.LogWarning("Project {ShortName} not found in database — possible orphaned Keycloak role. Skipping template cache warm-up", projectShortName.Value);
            return ([], false, false, []);
        }

        var projectDetails = await GetProjectDetailAsync(projectShortName, ct);
        if (projectDetails.Count == 0)
        {
            logger.LogWarning(
                "Project {ProjectShortName}: no HEAppE links found — no HPC/Quantum locations configured or all filtered by IsDeleted/type/missing HEAPPE_IDENTIFIER",
                projectShortName.Value);
        }

        // Dedup must include the HEAppE instance identifier: the same (ResourceName, LocationName) can
        // legitimately exist on two different HEAppE instances. Keying only by (ResourceName, LocationName)
        // collapsed them and silently dropped the second instance's templates.
        var distinctDetails = RoutableLinks(projectShortName, projectDetails)
            .DistinctBy(x => new { x.HEAppEApiIdentifier, x.ResourceName, x.LocationName })
            .ToList();

        var links = distinctDetails.Where(w => IsActiveOn(w.StartDate, w.EndDate, currentDate)).ToList();

        foreach (var expired in distinctDetails.Where(w => !IsActiveOn(w.StartDate, w.EndDate, currentDate)))
        {
            logger.LogWarning(
                "Project {ProjectShortName}: link '{LocationName}' (Resource:{ResourceName}, Identifier:{HeappeIdentifier}) is outside active date window — StartDate={StartDate}, EndDate={EndDate}, today={Today}",
                projectShortName.Value, expired.LocationName, expired.ResourceName, expired.HEAppEApiIdentifier,
                expired.StartDate, expired.EndDate, currentDate);
        }

        if (links.Count == 0 && projectDetails.Count > 0)
        {
            logger.LogWarning(
                "Project {ProjectShortName}: all {Total} HEAppE link(s) are outside the active date window (today={Today})",
                projectShortName.Value, projectDetails.Count, currentDate);
        }

        // Group by (HEAppE identifier, ResourceName) — same resource on same HEAppE instance shares one API call.
        // RoutableLinks dropped the links without an identifier, so GetValueOrDefault never yields the default.
        var linkGroups = links.GroupBy(l => (Identifier: l.HEAppEApiIdentifier.GetValueOrDefault(), ResourceName: l.ResourceName)).ToList();

        // Project-level precondition (ADR 0074): read the active command templates ONCE, before the HEAppE
        // fan-out, and pass them down to every group. A failure here is on our side (the Heappe integration
        // DB), not a HEAppE outage — so it fast-fails as an internal error (500) instead of masquerading as a
        // 503 total degradation, and it does so before any instance is queried. Skipped when there is nothing
        // to query, so a project with no active links never hits the DB.
        IReadOnlyCollection<CommandTemplatePartitionResponse> activeCommandTemplates = [];
        if (linkGroups.Count > 0)
        {
            var commandTemplatesResult = await heappeService.GetActiveCommandTemplatesAsync(project.Id, ct);
            if (!commandTemplatesResult.IsSuccess)
            {
                logger.LogError(
                    "TemplatesInfo for project {ProjectShortName}: active command templates could not be read ({Error}) — failing fast as an internal error",
                    projectShortName.Value, commandTemplatesResult.Error?.Description);
                throw new TemplatesInfoPreconditionException(
                    $"Active command templates for project '{projectShortName.Value}' could not be read.");
            }

            activeCommandTemplates = commandTemplatesResult.Value ?? [];
        }

        var results = await Task.WhenAll(
            linkGroups.Select(group =>
                ProcessIdentifierGroupAsync(project.ShortName, project.Id, group.Key.Identifier, group.Key.ResourceName, group.ToList(), currentDate, bearerToken, activeCommandTemplates, noCache, ct)));
        var response = results.SelectMany(r => r.Responses).ToList();
        var degraded = results.Any(r => r.Degraded);
        // Total outage only when at least one query was attempted AND every one of them failed. With no
        // link groups (no active links) results is empty — that is a valid empty build, not an outage.
        // allocation-missing is excluded: the instance is up and answered, the data merely disagrees with the
        // UserOrg link, so it must never turn a build into a 503 — it stays a 200 with the unavailable header.
        var allDegraded = results.Length > 0
            && results.All(r => r.Degraded && r.Failure?.Cause != HeappeFailureCause.AllocationMissing);
        // Instances that did not return results (whole-instance failures), each with its sanitized descriptor
        // and cause. Surfaced as a diagnostic header on partial degradation and as the 503 body on total.
        var failures = results
            .Select(r => r.Failure)
            .Where(f => f is not null)
            .Select(f => f!)
            .Distinct()
            .ToList();

        // Entries without a LocationId are dropped from the response. Report them: silently discarding them
        // turns a populated build into an empty 200 with nothing in the log to explain it.
        var droppedWithoutLocationId = response.Where(x => string.IsNullOrEmpty(x.LocationId)).ToList();
        if (droppedWithoutLocationId.Count > 0)
        {
            logger.LogWarning(
                "Project {ProjectShortName}: {DroppedCount} of {TotalCount} built entr(ies) have no LocationId and are dropped from the response. Cluster names: [{Clusters}]",
                projectShortName.Value, droppedWithoutLocationId.Count, response.Count,
                string.Join(", ", droppedWithoutLocationId.Select(x => x.ClusterName)));
        }

        var mergedResponse = new List<TemplateInfoResponse>();
        foreach (var group in response.Where(x => !string.IsNullOrEmpty(x.LocationId)).GroupBy(x => (x.LocationId, x.ClusterName, x.IsAvailable)))
        {
            if (group.Count() == 1)
            {
                mergedResponse.Add(group.First());
                continue;
            }

            var nodeGroups =
                group.SelectMany(x => x.Nodes).GroupBy(x => (x.NodeId, x.NodeType, x.FileTransferMethodId, x.NumberOfNodes, x.ClusterPerNode, x.MaxWallTime));

            var newNodeGrp = new List<NodeInfo>();
            foreach (var nodeGrp in nodeGroups)
            {
                newNodeGrp.Add(new NodeInfo(nodeGrp.Key.NodeId,
                    nodeGrp.Key.NodeType,
                    nodeGrp.Key.NumberOfNodes,
                    nodeGrp.Key.ClusterPerNode,
                    nodeGrp.Key.MaxWallTime,
                    nodeGrp.Key.FileTransferMethodId,
                    nodeGrp.SelectMany(a => a.Allocations)));

            }

            mergedResponse.Add(group.First() with { Nodes = newNodeGrp });
        }

        logger.LogInformation(
            "TemplatesInfo build for project {ProjectShortName}: links {ActiveLinks}/{TotalLinks} active, {LinkGroups} HEAppE query group(s), {ActiveTemplates} active command template(s), {RawEntries} raw entr(ies) -> {MergedEntries} after merge. Degraded={Degraded}, AllDegraded={AllDegraded}",
            projectShortName.Value, links.Count, distinctDetails.Count, linkGroups.Count,
            activeCommandTemplates.Count, response.Count, mergedResponse.Count, degraded, allDegraded);

        return (mergedResponse, degraded, allDegraded, failures);
    }

    // A link without a valid HEAPPE_IDENTIFIER cannot be routed through the NexusApi proxy; skip it with one warning.
    private List<ProjectHEAppELinks> RoutableLinks(ProjectShortName projectShortName, IList<IProjectHEAppELinks> details)
    {
        var routable = new List<ProjectHEAppELinks>();
        foreach (var link in details.Cast<ProjectHEAppELinks>())
        {
            if (link.HEAppEApiIdentifier is null)
            {
                logger.LogWarning(
                    "Project {ProjectShortName}: link '{LocationName}' (Resource:{ResourceName}) has no HEAPPE_IDENTIFIER and is skipped",
                    projectShortName.Value, link.LocationName, link.ResourceName);
                continue;
            }

            routable.Add(link);
        }

        return routable;
    }

    /// <summary>
    /// Calls GetListAvailableClusters once for all links that share the same HEAppE identifier and ResourceName,
    /// then applies per-link (per-LocationName) filtering on the shared cluster result.
    /// </summary>
    private async Task<(List<TemplateInfoResponse> Responses, bool Degraded, HeappeFailureInfo? Failure)> ProcessIdentifierGroupAsync(
        ProjectShortName projectShortName,
        ProjectId projectId,
        HeappeIdentifier identifier,
        string resourceName,
        List<ProjectHEAppELinks> links,
        DateTime currentDate,
        string bearerToken,
        IReadOnlyCollection<CommandTemplatePartitionResponse> activeCommandTemplates,
        bool noCache,
        CancellationToken ct)
    {
        var templateInfoResponses = new List<TemplateInfoResponse>();
        IList<ClusterInfoModel> heappeClusters;

        // Provider/resource descriptor for the "instance did not return results" header (ADR 0073). The raw
        // HEAppE identifier is deliberately NOT exposed — provider + resource + the affected locations identify the
        // source for the caller without leaking the instance endpoint.
        var provider = links[0].ProviderName;
        var locations = string.Join(", ", links.Select(l => l.LocationName));
        var unavailableInstance = string.IsNullOrEmpty(provider)
            ? $"{resourceName} ({locations})"
            : $"{provider} / {resourceName} ({locations})";

        try
        {
            var heappeResponse = await heappeService.GetListAvailableClusters(
                projectShortName, projectId, resourceName, identifier, bearerToken,
                links.Select(l => l.LocationName).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList(),
                activeCommandTemplates, ct, noCache);

            // Defensive: the adapter/service may return null (e.g., when mocked in tests) so guard against it
            if (heappeResponse is null)
            {
                logger.LogWarning(
                    "HEAppE returned null response for {HeappeIdentifier} (Resource:{ResourceName}, Locations:[{Locations}])",
                    identifier, resourceName, string.Join(", ", links.Select(l => l.LocationName)));
                links.ForEach(l => templateInfoResponses.Add(new(false, l.LocationName, l.LocationId, null, [])));
                return (templateInfoResponses, true, new HeappeFailureInfo(unavailableInstance, HeappeFailureCause.Internal));
            }

            if (!heappeResponse.IsSuccess)
            {
                logger.LogWarning(
                    "HEAppE returned non-success status for {HeappeIdentifier} (Resource:{ResourceName}, Locations:[{Locations}]). Error: {Error}",
                    identifier, resourceName, string.Join(", ", links.Select(l => l.LocationName)), heappeResponse.Error?.Description);
                links.ForEach(l => templateInfoResponses.Add(new(false, l.LocationName, l.LocationId, null, [])));
                // The cause travels on the Error.Code the adapter stamped (URL-free); the Error.Description —
                // which carries the URL and response body — is deliberately not propagated (ADR 0074).
                var cause = string.IsNullOrEmpty(heappeResponse.Error?.Code) ? HeappeFailureCause.Internal : heappeResponse.Error.Code;
                return (templateInfoResponses, true, new HeappeFailureInfo(unavailableInstance, cause));
            }

            heappeClusters = heappeResponse.Value ?? [];
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(e,
                "Request timed out (TaskCanceledException) while calling HEAppE {HeappeIdentifier} (Resource:{ResourceName}, Locations:[{Locations}])",
                identifier, resourceName, string.Join(", ", links.Select(l => l.LocationName)));
            links.ForEach(l => templateInfoResponses.Add(new(false, l.LocationName, l.LocationId, null, [])));
            return (templateInfoResponses, true, new HeappeFailureInfo(unavailableInstance, HeappeFailureCause.Timeout));
        }
        catch (TaskCanceledException e)
        {
            logger.LogWarning(e,
                "Request canceled while calling HEAppE {HeappeIdentifier} (Resource:{ResourceName})",
                identifier, resourceName);
            links.ForEach(l => templateInfoResponses.Add(new(false, l.LocationName, l.LocationId, null, [])));
            return (templateInfoResponses, true, new HeappeFailureInfo(unavailableInstance, HeappeFailureCause.Timeout));
        }
        catch (Exception e)
        {
            logger.LogError(e,
                "Unexpected error while calling HEAppE {HeappeIdentifier} (Resource:{ResourceName})",
                identifier, resourceName);
            links.ForEach(l => templateInfoResponses.Add(new(false, l.LocationName, l.LocationId, null, [])));
            return (templateInfoResponses, true, new HeappeFailureInfo(unavailableInstance, HeappeFailureCause.Internal));
        }

        var locationMissingFromPopulatedCatalog = false;
        foreach (var link in links)
        {
            var matchingClusters = heappeClusters
                .Where(r => r.Name.Equals(link.LocationName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!matchingClusters.Any())
            {
                var availableNames = heappeClusters.Select(c => c.Name).ToList();
                logger.LogWarning(
                    "HEAppE ({HeappeIdentifier}) returned {ClusterCount} cluster(s) but none matched LocationName '{LocationName}' (Resource:{ResourceName}). Available cluster names: [{Clusters}]",
                    identifier, availableNames.Count, link.LocationName, resourceName,
                    string.Join(", ", availableNames));
                templateInfoResponses.Add(new(false, link.LocationName, link.LocationId, null, []));

                // The adapter returns only clusters it actually found in the live catalog (a missing/partial
                // cluster is a plain per-cluster cache miss, never a stored empty snapshot). So an unmatched
                // active link means the instance did not return this cluster — it is still hydrating after
                // wake-up or the link is misconfigured. Mark the build degraded so the composed TemplatesInfo
                // snapshot is NOT cached: the retry refetches and self-heals once the cluster reappears, and a
                // genuine outage surfaces as 503 rather than a cached empty 200.
                //
                // Gated on the cache-backed path (!noCache): a noCache request is a pure pass-through that
                // returns the live (possibly partial) result as-is, never escalating to a degraded retry.
                if (!noCache)
                    locationMissingFromPopulatedCatalog = true;

                continue;
            }

            // Set LocationId on matched clusters (mirrors original code)
            foreach (var cluster in matchingClusters)
                cluster.LocationId = link.LocationId;

            link.IsAvailable = true;
            templateInfoResponses.AddRange(BuildTemplateResponses(link, matchingClusters, currentDate));
        }

        // No cache eviction needed: per-cluster keys mean a missing cluster was never cached, so the degraded
        // retry refetches it live without disturbing the other clusters/projects on the same instance.
        // A missing cluster in a populated catalog is a misconfiguration (or mid-hydration), reported as such.
        return (templateInfoResponses, locationMissingFromPopulatedCatalog,
            locationMissingFromPopulatedCatalog ? new HeappeFailureInfo(unavailableInstance, HeappeFailureCause.Misconfigured) : null);
    }

    /// <summary>
    /// Whether an allocation window covers <paramref name="today"/>, compared on whole days.
    /// </summary>
    /// <remarks>
    /// Allocations are day-granular on both sides, but the two sides disagree on time-of-day: UserOrg stores
    /// the window truncated to midnight, while HEAppE returns the real creation time (e.g. 10:29). Comparing
    /// raw <see cref="DateTime"/> values against <see cref="DateTime.Today"/> (midnight) therefore hid a
    /// project created earlier the same day until the next midnight rolled over. Both bounds are inclusive:
    /// an allocation starting or ending today is active today.
    /// </remarks>
    private static bool IsActiveOn(DateTime start, DateTime end, DateTime today)
        => start.Date <= today.Date && today.Date <= end.Date;

    /// <summary>
    /// Converts matched HEAppE clusters into <see cref="TemplateInfoResponse"/> entries,
    /// applying per-node and per-command-template filtering with diagnostic logging at each step.
    /// </summary>
    private IEnumerable<TemplateInfoResponse> BuildTemplateResponses(
        ProjectHEAppELinks link,
        List<ClusterInfoModel> clusters,
        DateTime currentDate)
    {
        foreach (var cluster in clusters)
        {
            if (!cluster.NodeTypes.Any())
            {
                logger.LogDebug(
                    "Cluster '{ClusterName}' (Location:{LocationName}, Resource:{ResourceName}) has no node types — skipping",
                    cluster.Name, link.LocationName, link.ResourceName);
                continue;
            }

            var matchingNodeTypes = cluster.NodeTypes
                .Where(nt => nt.Projects.Any(p =>
                    p.AccountingString.Equals(link.ResourceName, StringComparison.OrdinalIgnoreCase)
                    && IsActiveOn(p.StartDate, p.EndDate, currentDate)))
                .ToList();

            if (!matchingNodeTypes.Any())
            {
                // The Any() above ANDs two conditions, so a bare "no match" is ambiguous. Split them: report
                // which one failed, and for name-matches print the date window that was rejected. The per-node
                // Debug below never runs in this branch (matchingNodeTypes is empty), so this is the only place
                // the cause can surface. Round-trip ("o") dates and quoted accounting strings on purpose —
                // they expose a time-of-day/Kind mismatch or stray whitespace that a plain "d" format hides.
                var candidates = cluster.NodeTypes.SelectMany(nt => nt.Projects).ToList();
                var nameMatches = candidates
                    .Where(p => p.AccountingString.Equals(link.ResourceName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                logger.LogWarning(
                    "Cluster '{ClusterName}' (Location:{LocationName}): no node type has a project matching AccountingString='{ResourceName}' with an active date range. Cause={Cause}{NewLine}  today (DateTime.Today) : {Today}{NewLine}  validity in UserOrg    : {UserOrgValidity} (source: ProjectHEAppELinks){NewLine}  validity in HEAppE     : {HeappeValidity} (source: ListAvailableClusters){NewLine}  accounting string      : {NameMatchCount}/{CandidateCount} matched; all: [{Strings}]",
                    cluster.Name, link.LocationName, link.ResourceName,
                    nameMatches.Count == 0 ? "AccountingStringMismatch" : "OutsideDateWindow", Environment.NewLine,
                    $"{currentDate:o}", Environment.NewLine,
                    $"{link.StartDate:o} – {link.EndDate:o}", Environment.NewLine,
                    nameMatches.Count == 0
                        ? "(none — accounting string did not match, so no window to compare)"
                        : string.Join(", ", nameMatches.Select(p => $"{p.StartDate:o} – {p.EndDate:o}")),
                    Environment.NewLine,
                    nameMatches.Count, candidates.Count,
                    string.Join(", ", candidates.Select(p => $"'{p.AccountingString}'").Distinct()));
                continue;
            }

            var nodes = new List<NodeInfo>();
            foreach (var n in matchingNodeTypes)
            {
                if (n.Projects.All(p => !IsActiveOn(p.StartDate, p.EndDate, currentDate)))
                {
                    logger.LogDebug(
                        "Node {NodeId}/{NodeName} in cluster '{ClusterName}': all projects are outside the active date range (today={Today}) — node excluded. Project date ranges: [{Ranges}]",
                        n.Id, n.Name, cluster.Name, currentDate,
                        string.Join(", ", n.Projects.Select(p => $"{p.StartDate:d}–{p.EndDate:d}")));
                    continue;
                }

                var commandTemplates = n.Projects
                    .Where(pr => pr.AccountingString.Equals(link.ResourceName, StringComparison.OrdinalIgnoreCase)
                                 && link.EndDate.Date >= currentDate.Date)
                    .Where(w => IsActiveOn(w.StartDate, w.EndDate, currentDate))
                    .SelectMany(prm => prm.CommandTemplates)
                    .Select(c => new CommandTemplate(c.Id, c.Name, c.IsGeneric, c.TemplateParameters))
                    .ToList();

                if (!commandTemplates.Any())
                {
                    logger.LogWarning(
                        "Node {NodeId}/{NodeName} in cluster '{ClusterName}' (Resource:{ResourceName}, Location:{LocationName}): no command templates after filtering. LinkEndDate={LinkEndDate} (active={LinkActive}), today={Today}. Matching projects (AccountingString+date): {MatchCount}",
                        n.Id, n.Name, cluster.Name, link.ResourceName, link.LocationName,
                        link.EndDate, link.EndDate.Date >= currentDate.Date, currentDate,
                        n.Projects.Count(pr =>
                            pr.AccountingString.Equals(link.ResourceName, StringComparison.OrdinalIgnoreCase)
                            && IsActiveOn(pr.StartDate, pr.EndDate, currentDate)));
                }

                var allocations = commandTemplates.Count > 0
                    ? (IEnumerable<NodeAllocation>)[new NodeAllocation(link.ResourceName, commandTemplates)]
                    : [];
                nodes.Add(new NodeInfo(n.Id, n.Name, n.NumberOfNodes, n.CoresPerNode, n.MaxWalltime, n.FileTransferMethodId, allocations));
            }

            yield return new TemplateInfoResponse(link.IsAvailable, cluster.Name, cluster.LocationId, cluster.Id, nodes);
        }
    }
}