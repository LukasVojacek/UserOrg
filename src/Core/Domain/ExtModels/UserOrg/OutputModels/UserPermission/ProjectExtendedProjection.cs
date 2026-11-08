using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;

/// <summary>
/// Lightweight read-model returned by <c>GetProjectsContainsUserExtendedAsync</c>.
/// Contains only the data consumed by <c>PopulateExtendedUserInfo</c>, avoiding
/// full entity graph loading.
/// </summary>
public sealed record ProjectExtendedProjection(
    ProjectId Id,
    ProjectShortName ShortName,
    IList<ProjectResourceDetailModel> Resources
);