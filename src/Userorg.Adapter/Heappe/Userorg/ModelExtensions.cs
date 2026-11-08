using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Contracts.Entries;
using Heappe.Contracts.Entries.Sync;
using Heappe.Contracts.HEappeApiDto.Responses;

using UserOrgAPIContracts = UserOrganizationManagement.Domain.Contracts;
using UserOrgAPIProject = UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;

namespace Userorg.Adapter.Heappe.Userorg;

public static class ModelExtensions
{

    public static UserOrgAPIProject.ClusterInfoModel ConvertToModel(this ClusterInfoModel clusterInfoModel)
    {
        ArgumentNullException.ThrowIfNull(clusterInfoModel);
        return new()
        {
            Id = clusterInfoModel.Id,
            Name = clusterInfoModel.Name,
            Description = clusterInfoModel.Description,
            NodeTypes = clusterInfoModel.NodeTypes.Select(nt => nt.ConvertToModel()).ToList()
        };
    }

    public static UserOrgAPIProject.NodeTypeModel ConvertToModel(this NodeTypeModel nodeType)
    {
        ArgumentNullException.ThrowIfNull(nodeType);
        return new()
        {
            Id = nodeType.Id,
            Name = nodeType.Name,
            Description = nodeType.Description,
            NumberOfNodes = nodeType.NumberOfNodes,
            CoresPerNode = nodeType.CoresPerNode,
            MaxWalltime = nodeType.MaxWalltime,
            FileTransferMethodId = nodeType.FileTransferMethodId,
            Projects = nodeType.Projects.Select(p => p.ConvertToModel()).ToList()
        };
    }

    public static UserOrgAPIProject.HEAppEProjectModel ConvertToModel(this ProjectModel project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new()
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            StartDate = project.StartDate,
            EndDate = project.EndDate,
            AccountingString = project.AccountingString,
            CommandTemplates = project.CommandTemplates.Select(ct => ct.ConvertToModel()).ToList()
        };
    }

    public static UserOrgAPIProject.CommandtemplateModel ConvertToModel(this CommandTemplateCompactModel commandTemplate)
    {
        ArgumentNullException.ThrowIfNull(commandTemplate);
        return new()
        {
            Id = commandTemplate.Id,
            Name = commandTemplate.Name,
            Description = commandTemplate.Description,
            ExtendedAllocationCommand = commandTemplate.ExtendedAllocationCommand,
            IsGeneric = commandTemplate.IsGeneric,
            TemplateParameters = commandTemplate.TemplateParameters.Select(tp => new UserOrgAPIProject.TemplateParameterModel
            {
                Identifier = tp.Identifier,
                Description = string.IsNullOrEmpty(tp.Description) ? "placeholder" : tp.Description
            }).ToList()
        };
    }

    public static ProjectAllocationInfoEntry ConvertToModel(this UserOrgAPIContracts.ProjectAllocationInfo projectAllocation)
    {
        ArgumentNullException.ThrowIfNull(projectAllocation);

        return new ProjectAllocationInfoEntry(
            Id: projectAllocation.Id.Value,
            ShortName: projectAllocation.ShortName.Value,
            Resources: projectAllocation.ResourceAllocations.Select(ConvertToModel).ToList()
         );
    }

    public static ResourceAllocationInfoEntry ConvertToModel(this UserOrgAPIContracts.ResourceAllocationInfo resourceAllocation)
    {
        ArgumentNullException.ThrowIfNull(resourceAllocation);

        return new ResourceAllocationInfoEntry(

            Id: resourceAllocation.Id,
            Name: resourceAllocation.Name,
            Locations: resourceAllocation.Locations.Select(ConvertToModel).ToList()
        );
    }

    private static LocationAllocationInfo ConvertToModel(UserOrgAPIContracts.LocationAllocationInfo locationAllocation)
    {
        ArgumentNullException.ThrowIfNull(locationAllocation);

        return new LocationAllocationInfo(
            LocationId: locationAllocation.LocationId,
            LocationName: locationAllocation.LocationName,
            AggregationName: locationAllocation.AggregationName,
            HeappeInstance: ConvertToModel(locationAllocation.HeappeInstance)
        );
    }

    public static ProjectResourceInfoEntry ConvertToModel(this UserOrgAPIContracts.ProjectResourceInfo projectResourceInfo)
    {
        ArgumentNullException.ThrowIfNull(projectResourceInfo);
        return new ProjectResourceInfoEntry(
            Id: projectResourceInfo.Id,
            Name: projectResourceInfo.Name,
            ShortName: projectResourceInfo.ShortName,
            Description: projectResourceInfo.Description,
            StartDate: projectResourceInfo.StartDate,
            EndDate: projectResourceInfo.EndDate,
            Resource: ConvertToModel(projectResourceInfo.Resources.FirstOrDefault())
        );
    }

    private static ResourceInfoEntry ConvertToModel(this UserOrgAPIContracts.ResourceInfo? resourceInfo)
    {
        ArgumentNullException.ThrowIfNull(resourceInfo);
        return new ResourceInfoEntry(
            Id: resourceInfo.Id,
            Name: resourceInfo.Name,
            Description: resourceInfo.Description,
            StartDate: resourceInfo.StartDate,
            EndDate: resourceInfo.EndDate
        );
    }

    private static HeappeInstanceEntry ConvertToModel(UserOrgAPIContracts.HeappeInstance heappeInstance)
    {
        ArgumentNullException.ThrowIfNull(heappeInstance);
        return new HeappeInstanceEntry(heappeInstance.Uri, heappeInstance.Version, heappeInstance.Identifier);
    }

    public static ProjectResourceInfoSyncEntry ConvertToSyncModel(this UserOrgAPIContracts.ProjectResourceInfo projectResourceInfo)
    {
        ArgumentNullException.ThrowIfNull(projectResourceInfo);
        return new ProjectResourceInfoSyncEntry(
            Id: projectResourceInfo.Id,
            Name: projectResourceInfo.Name,
            ShortName: projectResourceInfo.ShortName,
            Description: projectResourceInfo.Description,
            StartDate: projectResourceInfo.StartDate,
            EndDate: projectResourceInfo.EndDate,
            Resource: ConvertToSyncModel(projectResourceInfo.Resources.FirstOrDefault())
        );
    }

    private static ResourceInfoSyncEntry ConvertToSyncModel(this UserOrgAPIContracts.ResourceInfo? resourceInfo)
    {
        ArgumentNullException.ThrowIfNull(resourceInfo);
        return new ResourceInfoSyncEntry(
            Id: resourceInfo.Id,
            Name: resourceInfo.Name,
            Description: resourceInfo.Description,
            StartDate: resourceInfo.StartDate,
            EndDate: resourceInfo.EndDate,
            Locations: resourceInfo.LocationAllocations.Select(s => s.ConvertToSyncModel()).ToList()
        );
    }

    private static LocationAllocationInfoSyncEntry ConvertToSyncModel(this UserOrgAPIContracts.LocationAllocationInfo locationAllocation)
    {
        ArgumentNullException.ThrowIfNull(locationAllocation);
        return new LocationAllocationInfoSyncEntry(
            locationAllocation.LocationId,
            locationAllocation.LocationName,
            locationAllocation.LoginEndpoint,
            locationAllocation.LocationType,
            locationAllocation.AggregationName,
            ConvertToSyncModel(locationAllocation.HeappeInstance)
        );
    }

    private static HeappeInstanceSyncEntry ConvertToSyncModel(UserOrgAPIContracts.HeappeInstance heappeInstance)
    {
        ArgumentNullException.ThrowIfNull(heappeInstance);
        return new HeappeInstanceSyncEntry(heappeInstance.Uri, heappeInstance.Version, heappeInstance.Identifier);
    }
}