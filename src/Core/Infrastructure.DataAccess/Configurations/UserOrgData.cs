using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Configurations;

/// <summary>
/// UserOrganization data
/// </summary>
public partial class UserOrgData
{
    public IList<ProjectDomainType>? ProjectDomainTypes { get; init; }
    public IList<LocationType>? LocationTypes { get; init; }
    public IList<AllocationUnitType>? AllocationUnitTypes { get; init; }

    public IList<Country>? Countries { get; init; }
    public IList<AssignedSystemPermissionTypeToSystemRole>? AssignedSystemPermissionTypeToSystemRoles { get; init; }
}