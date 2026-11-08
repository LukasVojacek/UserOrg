using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IProjectTagRepository : IRepository<ProjectTag, Guid>
{
    Task<IList<ProjectTag>> GetAllByTagNameAsync(string? tagName, CancellationToken ct);
}