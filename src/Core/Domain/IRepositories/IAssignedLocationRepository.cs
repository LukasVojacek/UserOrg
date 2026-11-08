using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IAssignedLocationRepository : IRepository<AssignedLocation, Guid>
{
    Task<IList<AssignedLocation>> GetAllBySpecificationsAsync(Guid? ProjectResourceId, Guid? ProviderId, CancellationToken ct = default);
}