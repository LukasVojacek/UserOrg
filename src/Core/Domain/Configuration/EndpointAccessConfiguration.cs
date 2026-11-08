using UserOrganizationManagement.Domain.Authorization;

namespace UserOrganizationManagement.Domain.Configuration;

public sealed class EndpointAccessConfiguration
{
    public IList<EndpointInformation> EndpointInformation { get; } = [];
}