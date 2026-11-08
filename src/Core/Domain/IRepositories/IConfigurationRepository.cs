using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IConfigurationRepository : IRepository<Entities.Configuration, Guid>
{

}