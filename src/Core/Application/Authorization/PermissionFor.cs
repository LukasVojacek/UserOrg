using UserOrganizationManagement.Domain.Authorization;

namespace UserOrganizationManagement.Application.Authorization;

public record struct PermissionFor(IEnumerable<EndpointInformation> Endpoints) : IPermissionFor { }