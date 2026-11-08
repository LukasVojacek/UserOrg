using UserOrg.Shared;

namespace UserOrganizationManagement.Domain.Contracts;

public interface IProjectHEAppELinks
{
    Uri? HEAppEApi { get; }
    Version HEAppEApiVersion { get; }
    HeappeIdentifier? HEAppEApiIdentifier { get; }
    string ResourceName { get; }
    string ProviderName { get; }
    string LocationName { get; }
    string LocationId { get; }
    string ResponseMessage { get; }
}