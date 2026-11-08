using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.IRepositories.AbstractTypes;

namespace UserOrganizationManagement.Domain.IRepositories;

public interface IRequestRepository : IRepository<Request, Guid>
{
    Task<Request> GetByIdWithUserAndProjectUser(Guid requestId, CancellationToken ct);
    Task<IList<PublicRequestPrepareModel>> GetByIdForPublicEndpoint(Guid requestId, CancellationToken ct);
    Task<IList<Request>> GetRequestsForUser(string idpUserId, CancellationToken ct);
    Task<IList<Request>> GetRequestsForResource(string resourceName, CancellationToken ct);
    Task<List<Request>> GetByStateAndPm(ERequestState? state, ValueObjects.ProjectShortName? projectShortName, string? projectManager, ERequestType? type, CancellationToken ct);
    Task<List<Request>> GetForUser(ERequestState? state, ValueObjects.ProjectShortName? projectShortName, string idpUserId, ERequestType? type, CancellationToken ct);
}