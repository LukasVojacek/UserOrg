using Forge.Core.Features.Role.Services;

using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IProjectService
{
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
    /// that provider and the caller's own projects (see <paramref name="callerIdpUserId"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="callerIdpUserId">The calling ProviderAdmin's identity-provider user id. When
    /// <paramref name="providerId"/> is set, a project the caller is assigned to or created is included even
    /// outside the provider's resource chain. Ignored when <paramref name="providerId"/> is <c>null</c>.</param>
    /// <returns>A list of projects matching the filters. Returns empty list if no matches, or throws RecordNotFoundException
    /// if a filter was supplied and no results were found.</returns>
    Task<IList<ProjectModel>> GetProjectsAsync(ProjectShortName? shortName, string? contactEmail, int? domainId, DateTime? startDate, DateTime? endDate, Guid? providerId, CancellationToken ct, string? callerIdpUserId = null);
    Task<List<ProjectModel>> GetProjectsAsync(ProjectShortName? projectShortName, string idpUserId, CancellationToken ct);
    Task<ProjectModel> GetProjectAsync(ProjectShortName projectShortName, CancellationToken ct);
    Task<ExtendedProjectModel> GetExtendedProjectInfoAsync(ProjectShortName projectShortName, CancellationToken ct);
    Task<IList<IProjectHEAppELinks>> GetProjectDetailAsync(ProjectShortName projectShortName, CancellationToken ct);
    Task<Guid> InsertProjectAsync(ProjectCreate projectCreate, Guid createdByUserId, CancellationToken ct);
    Task UpdateProjectAsync(ProjectUpdate projectUpdate, CancellationToken ct);
    Task DeleteProjectAsync(ProjectShortName projectShortName, CancellationToken ct);
    Task EditAssignedUserSystemRole(ProjectShortName projectShortName, string idpUserId, string idpRequesterUserId, int systemRoleId, bool isAdmin, CancellationToken ct);
    Task EditAssignedUserSystemRole(ProjectShortName projectShortName, string idpUserId, string idpRequesterUserId, string systemRoleName, CancellationToken ct);
    Task AssignedProjectResourceToProjectAsync(ProjectShortName projectShortName, string projectResourceName, Guid? providerId, CancellationToken ct);
    Task UnAssignedProjectResourceToProjectAsync(ProjectShortName projectShortName, string projectResourceName, CancellationToken ct);
    Task AssignedUserToProjectWithRoleAsync(ProjectShortName projectShortName, string idpUserId, Guid assignedByUserId, string roleName, CancellationToken ct);
    Task UnAssignedUserFromProjectAsync(ProjectShortName projectShortName, string idpUserId, Guid unassignedByUserId, CancellationToken ct);
    Task AssignedProjectTagToProjectAsync(ProjectShortName projectShortName, Guid projectTagId, CancellationToken ct);
    Task UnAssignedProjectTagToProjectAsync(ProjectShortName projectShortName, Guid projectTagId, CancellationToken ct);
    Task<IList<ProjectUserModel>> GetProjectUsersWithRoles(ProjectShortName projectShortName, CancellationToken ct);
    Task<RoleInfo?> GetProjectRole(ProjectShortName projectShortName, string idpUserId, CancellationToken ct);
    Task<IList<Project>> GetProjectsForProvider(Guid providerId, CancellationToken ct);
    Task<TemplatesInfoResult> ProjectTemplateInfo(ProjectShortName projectShortName, string bearerToken, bool noCache, CancellationToken ct);

}

/// <summary>
/// Result of a TemplatesInfo build: the per-cluster <see cref="TemplateInfoResponse"/> entries returned to the
/// caller, plus the HEAppE instances that did not return results. The endpoint surfaces
/// <see cref="UnavailableInstances"/> as a diagnostic response header (NOT in the serialized body), so the
/// OpenAPI contract is unchanged. The list is empty whenever every queried instance answered.
/// </summary>
/// <remarks>
/// TODO (#219): the header is a stopgap while the OpenAPI schema / response models are frozen. The refactor
/// version should fold the unavailable-instance information directly into the response object and remove both
/// this side-channel and the X-Unavailable-HEAppE-Instances header.
/// </remarks>
public sealed record TemplatesInfoResult(List<TemplateInfoResponse> Responses, IReadOnlyList<string> UnavailableInstances);