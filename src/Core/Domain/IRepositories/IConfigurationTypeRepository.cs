using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IConfigurationTypeRepository : IRepository<ConfigurationType, int>
{

}