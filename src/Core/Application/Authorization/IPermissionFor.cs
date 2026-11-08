using UserOrganizationManagement.Domain.Authorization;

namespace UserOrganizationManagement.Application.Authorization;

public interface IPermissionFor
{
    IEnumerable<EndpointInformation> Endpoints { get; }
}