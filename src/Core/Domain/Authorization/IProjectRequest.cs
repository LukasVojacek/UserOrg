using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Authorization;

/// <summary>
/// For input object on endpoints for Authorization
/// </summary>
public interface IProjectRequest
{
    ProjectShortName ProjectShortName { get; set; }
}