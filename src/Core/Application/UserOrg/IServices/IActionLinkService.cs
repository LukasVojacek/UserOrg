using Microsoft.AspNetCore.Http;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IActionLinkService
{
    Task<(string AcceptLink, string RejectLink)> GenerateActionLinks(HostString baseUri, Guid requestId, CancellationToken ct);
    Task<bool> Handle(string actionLink, IProjectRequestService requestService, CancellationToken ct);
}