using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IActionLinkRepository : IRepository<ActionLink, Guid>
{
    Task<ActionLink> GetByLink(string actionLink, CancellationToken ct);
    Task<IList<ActionLink>> GetRelatedToRequest(Guid requestId, CancellationToken ct);
}