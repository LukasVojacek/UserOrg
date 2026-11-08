using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.IRepositories;

namespace UserOrganizationManagement.Application.UserOrg.Services;

public sealed class ActionLinkService : IActionLinkService
{
    private readonly Domain.Configuration.ActionLink _linkConfiguration;
    private readonly IUserOrgUnitOfWork _uow;
    private readonly IRequestRepository _requestRepository;

    public ActionLinkService(IOptions<Domain.Configuration.ActionLink> options, IUserOrgUnitOfWork uow, IRequestRepository requestRepository)
    {
        ArgumentNullException.ThrowIfNull(options);
        _linkConfiguration = options.Value;
        _uow = uow;
        _requestRepository = requestRepository;
    }

    public async Task<(string AcceptLink, string RejectLink)> GenerateActionLinks(HostString baseUri, Guid requestId, CancellationToken ct)
    {
        Request request = await _requestRepository.GetByIdAsync(requestId, ct);
        ArgumentNullException.ThrowIfNull(request);

        var linkIdAccept = Guid.NewGuid().ToString().Replace("-", "", StringComparison.OrdinalIgnoreCase);
        var linkIdReject = Guid.NewGuid().ToString().Replace("-", "", StringComparison.OrdinalIgnoreCase);

        var acceptLink = $"{baseUri}/api/userorg/api/Action/Handle/{linkIdAccept}";
        var rejectLink = $"{baseUri}/api/userorg/api/Action/Handle/{linkIdReject}";

        IList<ActionLink> existingLinks = await _uow.ActionLinkRepository.GetRelatedToRequest(requestId, ct);

        if (existingLinks.Any())
        {
            throw new RecordAlreadyExistException($"Action links for request id: {requestId} are not yet handled");
        }

        await _uow.ActionLinkRepository.InsertAsync(new Domain.Entities.ActionLink()
        {
            Action = Domain.Enums.EActionType.Accept,
            Link = linkIdAccept,
            ValidTo = DateTime.UtcNow.Add(_linkConfiguration.Duration),
            Request = request,
            RequestId = request.Id
        }, ct);

        await _uow.ActionLinkRepository.InsertAsync(new Domain.Entities.ActionLink()
        {
            Action = Domain.Enums.EActionType.Reject,
            Link = linkIdReject,
            ValidTo = DateTime.UtcNow.Add(_linkConfiguration.Duration),
            Request = request,
            RequestId = request.Id
        }, ct);

        var changes = await _uow.SaveAsync(ct);

        return changes > 0 ? ((string AcceptLink, string RejectLink))(acceptLink, rejectLink) : ((string AcceptLink, string RejectLink))("-1", "-1");
    }

    public async Task<bool> Handle(string actionLink, IProjectRequestService requestService, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requestService);

        ActionLink link = await _uow.ActionLinkRepository.GetByLink(actionLink, ct) ?? throw new ArgumentException("Invalid link");

        IList<ActionLink> relatedLinks = await _uow.ActionLinkRepository.GetRelatedToRequest(link.RequestId, ct);

        foreach (ActionLink relatedLink in relatedLinks)
        {
            relatedLink.IsDeleted = true;
        }

        var handled = await SettleRequest(link, requestService, ct);

        // Not a no-op: settling does not always save. The burn shares the scoped DbContext with the
        // settling service, so it is normally flushed - and committed - by that service's own save.
        // This save is the ONLY one for the paths that settle without writing anything:
        // DeclineAssignRequestAsync's early return for an already accepted/rejected request, and an
        // accept that matched no branch in AcceptResourceAssignmentAsync. Deleting it strands those
        // links unburned and reusable.
        // Consequence on the transacted resource accept: a rollback there reverts the burn as well, so
        // the link stays usable and the PI can retry - "invalidated on use" is now "on successful use".
        // See docs/architecture/adr/0087-transacted-resource-accept-and-conditional-link-burn.md.
        await _uow.SaveAsync(ct);
        return handled;
    }

    private static async Task<bool> SettleRequest(ActionLink link, IProjectRequestService requestService, CancellationToken ct)
    {
        if (link.Action == Domain.Enums.EActionType.Accept)
        {
            await requestService.AcceptAssignRequestAsync(link.RequestId, ct);
            return true;
        }

        if (link.Action == Domain.Enums.EActionType.Reject)
        {
            await requestService.DeclineAssignRequestAsync(link.RequestId, "", ct);
            return true;
        }

        return false;
    }
}