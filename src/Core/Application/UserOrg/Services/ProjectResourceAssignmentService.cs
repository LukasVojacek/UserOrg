using Core.Domain.Exceptions;

using Microsoft.Extensions.Caching.Hybrid;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResourceAssignment;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

namespace UserOrganizationManagement.Application.UserOrg.Services;

/// <summary>
/// Constructor
/// </summary>
/// <param name="userOrgUnitOfWork">UserOrg UnitOfWork</param>
public class ProjectResourceAssignmentService(IUserOrgUnitOfWork userOrgUnitOfWork, IHeappeSynchronizationService heappeSynchronizationService, HybridCache cache) : IProjectResourceAssignmentService
{

    #region Constructors
    #endregion

    public async Task<IList<ProjectResourceAssignmentModel>> GetProjectResourceAssignmentsAsync(Guid? providerId, Guid? projectResourceId, CancellationToken ct = default)
    {
        IList<AssignedLocation> projectResourceAssignments = await userOrgUnitOfWork.AssignedLocationRepository.GetAllBySpecificationsAsync(projectResourceId, providerId, ct);

        if ((projectResourceId is not null || providerId is not null) && projectResourceAssignments.Count == 0)
        {
            var projResource = projectResourceId is null ? string.Empty : $"projectResourceId:{projectResourceId}";
            var provider = providerId is null ? string.Empty : $"providerId:{providerId}";
            throw new RecordNotFoundException($"""Project Resource Assignment for input parameters: {projResource},  {provider} do not exist!""");
        }

        return projectResourceAssignments.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<ProjectResourceAssignmentModel> GetProjectResourceAssignmentAsync(Guid? providerId, Guid id, CancellationToken ct = default)
    {
        IList<AssignedLocation> projectResourceAssignments = await userOrgUnitOfWork.AssignedLocationRepository
            .GetAllBySpecificationsAsync(id, null, ct)
             ?? throw new RecordNotFoundException($"""Project Resource Assignment with id:"{id}" does not exist!""");

        AssignedLocation projectResourceAssignment = projectResourceAssignments
            .Where(al => al.Location.LocationType.Id == 1) // 1 == HPC type
            .FirstOrDefault(x => x.AllocationUnitType.Id > 1) ?? throw new RecordNotFoundException($"""HPC Project Resource Assignment with id:"{id}" does not exist!""");


        return providerId.HasValue && projectResourceAssignment.Location.Provider.Id != providerId.Value
            ? throw new RecordNotFoundException($"""Project Resource Assignment with id:"{id}" does not exist!""")
            : await Task.FromResult(projectResourceAssignment.ConvertToModel());
    }

    public async Task<Guid> InsertProjectResourceAssignmentAsync(ProjectResourceAssignmentCreate obj, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        ProjectResource projectResource = await userOrgUnitOfWork.ProjectResourceRepository.GetByIdWithIncludesAsync(obj.ProjectResourceId!.Value, ct)
            ?? throw new RecordNotFoundException($"""Project Resource with id:"{obj.ProjectResourceId}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(projectResource.Mode);

        Location location = await userOrgUnitOfWork.LocationRepository.GetByIdWithAssignmentsAsync(obj.LocationId!.Value, ct)
            ?? throw new RecordNotFoundException($"""Location with id:"{obj.LocationId}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(location.Mode);

        AllocationUnitType allocationUnitType = await userOrgUnitOfWork.AllocationUnitTypeRepository.GetByIdAsync(obj.AllocationUnitTypeId!.Value, ct)
            ?? throw new RecordNotFoundException($"""AllocationUnitType with id:"{obj.AllocationUnitTypeId}" does not exist!""");

        if (projectResource.AssignedLocations.Any(x => !x.Location.IsShared))
        {
            throw new ResourceHasAssignedNonSharedLocationAlreadyException($"""Project Resource with id:"{obj.ProjectResourceId}" has already assigned non-shared location!""");
        }

        if (!location.IsShared && location.AssignedLocations.Any(x => !x.IsDeleted))
        {
            throw new NonSharedLocationAlreadyHasAssignedResourceException($"""Location with id:"{obj.LocationId}" has already assigned resource!""");
        }

        AssignedLocation projectResourceAssignment = obj.ConvertToEntity(projectResource, location, allocationUnitType);
        await userOrgUnitOfWork.AssignedLocationRepository.InsertAsync(projectResourceAssignment, ct);

        await userOrgUnitOfWork.SaveAsync(ct);

        foreach (var assignedProject in projectResource.AssignedProjects)
        {
            await heappeSynchronizationService.TriggerSynchronizationForProjectResource(assignedProject.Project.ShortName, projectResource.Name, ct);
            await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(assignedProject.Project.ShortName.Value), ct);
        }

        return projectResourceAssignment.Id;
    }
    public async Task UpdateProjectResourceAssignmentAsync(ProjectResourceAssignmentUpdate obj, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        AssignedLocation assignedLocation = await userOrgUnitOfWork.AssignedLocationRepository
            .GetByIdAsync(obj.Id!.Value, ct, i => i.AssignedLocationsValues,
                i => i.AllocationUnitType)
            ?? throw new RecordNotFoundException($"""Project Resource Assignment with id:"{obj.Id}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(assignedLocation.Mode);

        AllocationUnitType allocationUnitType = assignedLocation.AllocationUnitType;
        if (assignedLocation.AllocationUnitType.Id != obj.AllocationUnitTypeId)
        {
            _ = allocationUnitType = await userOrgUnitOfWork.AllocationUnitTypeRepository.GetByIdAsync(obj.AllocationUnitTypeId!.Value, ct)
                ?? throw new RecordNotFoundException($"""Allocation Unit type with id:"{obj.AllocationUnitTypeId}" does not exist!""");
        }

        await UpdateEntity(assignedLocation, obj, allocationUnitType);
        await userOrgUnitOfWork.SaveAsync(ct);

        var projectResourceAssigment = await userOrgUnitOfWork.AssignedLocationRepository.GetByIdAsync(obj.Id.Value, ct,
            i => i.ProjectResource,
            i => i.ProjectResource.AssignedProjects);

        if (projectResourceAssigment?.ProjectResource?.AssignedProjects is not null)
        {
            foreach (var assignedProject in projectResourceAssigment.ProjectResource.AssignedProjects)
            {
                // Ensure Project navigation is loaded; repository include for collection->reference navigation
                // using Select(...) is not supported in Include expressions here, so load explicitly if required
                var project = assignedProject.Project ?? await userOrgUnitOfWork.ProjectRepository.GetByIdAsync(assignedProject.ProjectId, ct);
                if (project is not null)
                {
                    await heappeSynchronizationService.TriggerSynchronizationForProjectResource(project.ShortName, projectResourceAssigment.ProjectResource.Name, ct);
                    await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(project.ShortName.Value), ct);
                }
            }
        }


        async Task UpdateEntity(AssignedLocation assignedLocation, ProjectResourceAssignmentUpdate obj, AllocationUnitType allocUnitType)
        {
            foreach (AssignedLocationValue assignedLocVal in assignedLocation.AssignedLocationsValues)
            {
                await userOrgUnitOfWork.AssignedLocationValueRepository.DeleteAsync(assignedLocVal.Id, ct);
            }

            for (var i = 0; i < obj.Specifications.Count; i++)
            {
                KeyValuePair<string, string> x = obj.Specifications[i];
                await userOrgUnitOfWork.AssignedLocationValueRepository.InsertAsync(new AssignedLocationValue()
                {
                    Name = x.Key,
                    Value = x.Value,
                    AssignedLocation = assignedLocation
                }, ct);
            }

            assignedLocation.AggregationName = obj.AggregationName;
            assignedLocation.AllocationUnitCount = obj.AllocationAmount;
            assignedLocation.AllocationUnitType = allocUnitType;
            assignedLocation.Description = obj.Description;
        }

    }
    public async Task DeleteProjectResourceAssignmentAsync(Guid id, CancellationToken ct = default)
    {
        AssignedLocation projectResourceAssignment = await userOrgUnitOfWork.AssignedLocationRepository.GetByIdAsync(id, ct)
            ?? throw new RecordNotFoundException($"""Project Resource Assignment with id:"{id}" does not exist!""");

        RecordReadOnlyException.ThrowIfReadOnly(projectResourceAssignment.Mode);

        // Load related ProjectResource and its AssignedProjects before deleting the AssignedLocation
        // Note: using Select(...) inside Include is not supported here, so load collection then explicitly ensure Project is loaded per item
        var projectResourceAssignmentWithIncludes = await userOrgUnitOfWork.AssignedLocationRepository.GetByIdAsync(projectResourceAssignment.Id, ct,
            i => i.ProjectResource,
            i => i.ProjectResource.AssignedProjects);

        await userOrgUnitOfWork.AssignedLocationRepository.DeleteAsync(projectResourceAssignment.Id, ct);
        await userOrgUnitOfWork.SaveAsync(ct);

        if (projectResourceAssignmentWithIncludes?.ProjectResource?.AssignedProjects is not null)
        {
            foreach (var assignedProject in projectResourceAssignmentWithIncludes.ProjectResource.AssignedProjects)
            {
                var project = assignedProject.Project ?? await userOrgUnitOfWork.ProjectRepository.GetByIdAsync(assignedProject.ProjectId, ct);
                if (project is not null)
                {
                    await heappeSynchronizationService.TriggerSynchronizationForProjectResource(project.ShortName, projectResourceAssignmentWithIncludes.ProjectResource.Name, ct);
                    await cache.RemoveByTagAsync(CacheTags.TemplatesInfo(project.ShortName.Value), ct);
                }
            }
        }
    }
}