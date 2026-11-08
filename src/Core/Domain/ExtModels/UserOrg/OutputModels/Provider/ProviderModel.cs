namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;

public class ProviderModel
{

    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Country { get; set; }
    public string? SupportEmail { get; set; }
    public EOperationModeResponse OperationMode { get; set; }
}