using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class ProjectDomainTypeRepository(DatabaseContext context) : GenericRepository<ProjectDomainType, int>(context), IProjectDomainTypeRepository
{
}