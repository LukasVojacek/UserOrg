using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class UserProjectAccessRepository(DatabaseContext context) : GenericRepository<UserProjectAccess, Guid>(context), IUserProjectAccessRepository
{

}