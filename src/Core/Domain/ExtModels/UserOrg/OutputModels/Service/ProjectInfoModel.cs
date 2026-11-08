using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Service;

public class ProjectInfoModel
{

    public string? ProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderDescription { get; set; }

    public IList<ProjectResourceServiceModel>? Resources { get; init; }
}