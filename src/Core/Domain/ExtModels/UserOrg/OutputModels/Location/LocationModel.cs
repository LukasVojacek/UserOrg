using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Location;

public class LocationModel
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Endpoint { get; set; }
    public ProviderModel? Provider { get; set; }
    public int? LocationTypeId { get; set; }
    public EOperationModeResponse OperationMode { get; set; }
    public bool IsShared { get; set; }
#pragma warning disable CA1056 // URI properties for API model should be strings
    public string? DefaultServiceIntegrationUrl { get; set; }
#pragma warning restore CA1056
}