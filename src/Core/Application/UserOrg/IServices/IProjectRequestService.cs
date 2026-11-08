using Microsoft.AspNetCore.Http;

using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IProjectRequestService
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="state">When not specified use <seealso cref="ERequestState.Pending"/> </param>
    /// <param name="ct">Cancellation token</param>
    /// 
    /// <returns>Returns all assign requests with specified <seealso cref="ERequestState">state</seealso>. </returns>
    Task<List<RequestModel>> GetRequestsAsync(string? projectMangerSid, ERequestState? state, ProjectShortName? projectShortName = null, ERequestType? type = null, CancellationToken ct = default);
    Task<List<RequestModel>> GetSentRequestsAsync(string idpUserId, ERequestState? state, ProjectShortName? projectShortName = null, ERequestType? type = null, CancellationToken ct = default);
    Task AddAssignRequestAsync(ProjectShortName projectShortName, string idpUserId, ERequestType type, string? projectResourceName, Guid? providerId, string? contactEmail, HostString baseUri, CancellationToken ct);
    Task<ERequestState> AcceptAssignRequestAsync(Guid requestId, Guid assignedByUserId, CancellationToken ct = default);
    Task<ERequestState> AcceptAssignRequestAsync(Guid requestId, CancellationToken ct = default);
    Task DeclineAssignRequestAsync(Guid requestId, string? reason, CancellationToken ct);

    Task<PublicRequestModel> GetPublicRequestInfoAsync(Guid requestId, CancellationToken ct = default);

}