using FluentValidation;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Location;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResourceAssignment;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectTag;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Provider;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Location;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectTag;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg;

public static class ConvertorExtensions
{
    public static ProjectModel ConvertToModel(this Project project)
    {
        ProjectNotFoundException.ThrowIfNull(project);
        return new()
        {
            Id = project.Id,
            Name = project.Name,
            ShortName = project.ShortName,
            Description = project.Description,
            ContactEmail = project.Email,
            StartDate = project.StartDate,
            EndDate = project.EndDate,
            DomainId = project.ProjectDomainType.Id,
            OperationMode = project.Mode.ToResponse(),
            AssignedProjectResourceIds = project.AssignedProjectResources.Where(w => !w.IsDeleted).Select(s => s.ProjectResource.Id).ToList(),
            AssignedTags = project.ProjectTags?.Where(w => !w.IsDeleted).Select(s => s.Id).ToList() ?? []
        };
    }

    public static ProjectResourceModel ConvertToModel(this ProjectResource projectRes)
    {
        ArgumentNullException.ThrowIfNull(projectRes);
        return new()
        {
            Id = projectRes.Id,
            Name = projectRes.Name,
            Description = projectRes.Description,
            PrimaryInvestigatorEmail = projectRes.PrimaryInvestigatorEmail,
            StartDate = projectRes.StartDate,
            EndDate = projectRes.EndDate,
            OperationMode = projectRes.Mode.ToResponse(),
            Assignments = projectRes.AssignedLocations.Where(w => !w.IsDeleted).Select(s => s.ConvertToModel()).ToList()
        };
    }

    public static ProjectResourceAssignmentModel ConvertToModel(this AssignedLocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        return new()
        {
            Id = allocation.Id,
            LocationId = allocation.Location.Id,
            LocationName = allocation.Location.Name,
            LocationTypeId = allocation.Location.LocationType.Id,
            ProjectResourceId = allocation.ProjectResource.Id,
            AllocationAmount = allocation.AllocationUnitCount,
            AllocationUnitTypeId = allocation.AllocationUnitType.Id,
            AggregationName = allocation.AggregationName,
            IsLocationShared = allocation.Location.IsShared,
            OperationMode = allocation.Mode.ToResponse(),
            Specifications = allocation.AssignedLocationsValues.Select(s => new KeyValuePair<string, string>(s.Name, s.Value)).ToList()
        };
    }

    public static ProjectTagModel ConvertToModel(this ProjectTag projectTag)
    {
        ArgumentNullException.ThrowIfNull(projectTag);
        return new()
        {
            Id = projectTag.Id,
            Name = projectTag.Name,
            Description = projectTag.Description
        };
    }

    public static ProviderModel ConvertToModel(this Provider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new()
        {
            Id = provider.Id,
            Name = provider.Name,
            Description = provider.Description,
            Country = provider.Country?.Name,
            SupportEmail = provider.SupportEmail,
            OperationMode = provider.Mode.ToResponse()
        };
    }

    public static LocationModel ConvertToModel(this Location loc)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return new()
        {
            Id = loc.Id,
            Name = loc.Name,
            Description = loc.Description,
            Endpoint = loc.Endpoint,
            Provider = loc.Provider.ConvertToModel(),
            LocationTypeId = loc.LocationType.Id,
            IsShared = loc.IsShared,
            OperationMode = loc.Mode.ToResponse(),
            DefaultServiceIntegrationUrl = loc.DefaultServiceIntegrationUrl?.ToString() ?? default
        };
    }

    public static CountryModel ConvertToModel(this Country country)
    {
        ArgumentNullException.ThrowIfNull(country);
        return new()
        {
            Code = country.Name,
            Name = country.Description
        };
    }

    public static RequestModel ConvertToModel(this Request req)
    {
        ArgumentNullException.ThrowIfNull(req);
        return new()
        {
            RequestId = req.Id,
            IdpUserId = req.User.IdentityProviderUserId,
            ProjectShortName = req.Project.ShortName,
            Type = (int)req.Type,
            State = (int)req.State,
            Email = req.User.Email,
            UserName = req.User.UserName,
            LastModification = req.ModifiedAt ?? req.CreatedAt

        };
    }
    public static PublicRequestModel ConvertToPublicModel(this IList<PublicRequestPrepareModel> req)
    {
        ArgumentNullException.ThrowIfNull(req);

        if (req.Count != 2)
        {
            throw new ValidationException($"Invalid count of active ActionLinks ({req.Count})");
        }

        PublicRequestPrepareModel accept = req.First(x => x.Action == Domain.Enums.EActionType.Accept);
        PublicRequestPrepareModel reject = req.First(x => x.Action == Domain.Enums.EActionType.Reject);
        return new()
        {
            RequestId = accept.RequestId,
            ProjectShortName = accept.ProjectShortName,
            ProjectResourceName = accept.ProjectResourceName,
            RequestorEmail = accept.RequestorEmail,
            RequestorUserName = accept.RequestorUserName,
            ProviderName = accept.ProviderName,
            RequestState = accept.RequestState,
            ValidTo = accept.ValidTo,
            RequestType = accept.RequestType,
            AcceptActionLink = accept.ActionLink,
            RejectActionLink = reject.ActionLink,
        };
    }

    public static ProjectResourceServiceModel ConvertToModelForService(this ProjectResource projectRes, string providerName)
    {
        ArgumentNullException.ThrowIfNull(projectRes);

        return new()
        {
            Id = projectRes.Id,
            Name = projectRes.Name,
            Description = projectRes.Description,
            StartDate = projectRes.StartDate,
            EndDate = projectRes.EndDate,
            AssociatedProjects = projectRes.AssignedProjects
                .Where(w => !w.IsDeleted)
                .Select(s => new AssociatedProjectModel(s.ProjectId, s.Project.Name, s.Project.ShortName.Value, $"{s.Project.ShortName.Value}|{providerName.Replace(' ', '_')}|{projectRes.Name}", s.Project.StartDate, s.Project.EndDate))
                .ToList(),
            Assignments = projectRes.AssignedLocations.Where(w => !w.IsDeleted).Select(s => s.ConvertToServiceModel()).ToList()
        };
    }

    public static ProjectResourceAssignmentServiceModel ConvertToServiceModel(this AssignedLocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        return new()
        {
            AllocationUnitTypeName = allocation.AllocationUnitType.Name,
            LocationDescription = allocation.Location.Description,
            ResourceType = allocation.Location.LocationType.Name,
            LocationName = allocation.Location.Name,
            AllocationAmount = allocation.AllocationUnitCount,
            AggregationName = allocation.AggregationName,
            Specifications = allocation.AssignedLocationsValues.Select(s => new KeyValuePair<string, string>(s.Name, s.Value)).ToList()
        };
    }


    public static Project ConvertToEntity(this ProjectCreate project, ProjectDomainType domain, User user)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new()
        {
            Name = project.Name,
            ShortName = ProjectShortName.Create(project.ShortName),
            Description = project.Description,
            Email = project.ContactEmail,
            StartDate = project.StartDate,
            EndDate = project.EndDate,
            ProjectDomainType = domain,
            Mode = EOperationMode.Editable,
            ServiceName = ApplicationConstants.DefaultServiceNameInDatabase,
            ExternalId = default,
            CreatedByUser = user
        };
    }

    public static ProjectResource ConvertToEntity(this ProjectResourceCreate projectRes)
    {
        ArgumentNullException.ThrowIfNull(projectRes);
        return new()
        {
            Name = projectRes.Name,
            Description = projectRes.Description,
            PrimaryInvestigatorEmail = projectRes.PrimaryInvestigatorEmail!,
            StartDate = projectRes.StartDate,
            EndDate = projectRes.EndDate,
            Mode = EOperationMode.Editable,
            ServiceName = ApplicationConstants.DefaultServiceNameInDatabase,
            ExternalId = default
        };
    }

    public static AssignedLocation ConvertToEntity(this ProjectResourceAssignmentCreate resAllocation, ProjectResource projectRes, Location location, AllocationUnitType allocationType)
    {
        ArgumentNullException.ThrowIfNull(allocationType);
        ArgumentNullException.ThrowIfNull(resAllocation);
        return new()
        {
            ProjectResource = projectRes,
            Location = location,
            AllocationUnitCount = resAllocation.AllocationAmount,
            AllocationUnitType = allocationType,
            AggregationName = resAllocation.AggregationName,
            Mode = EOperationMode.Editable,
            ServiceName = ApplicationConstants.DefaultServiceNameInDatabase,
            AssignedLocationsValues = resAllocation.Specifications?.Select(ConvertToEntity).ToList() ?? []
        };
    }

    public static AssignedLocationValue ConvertToEntity(KeyValuePair<string, string> specifications) => new()
    {
        Name = specifications.Key,
        Value = specifications.Value
    };

    public static ProjectTag ConvertToEntity(this ProjectTagCreate projectTag, User createdByUser)
    {
        ArgumentNullException.ThrowIfNull(projectTag);
        return new()
        {
            Name = projectTag.Name,
            Description = projectTag.Description,
            CreatedByUser = createdByUser
        };
    }

    public static Provider ConvertToEntity(this ProviderCreate provider, Country country)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new()
        {
            Name = provider.Name,
            Description = provider.Description,
            IsPrivate = provider.IsPrivate,
            Country = country,
            SupportEmail = provider.SupportEmail,
            Mode = EOperationMode.Editable,
            ServiceName = ApplicationConstants.DefaultServiceNameInDatabase,
            ExternalId = default
        };
    }

    public static Location ConvertToEntity(this LocationCreate loc, Provider provider, LocationType type)
    {
        ArgumentNullException.ThrowIfNull(loc);
        return new()
        {
            Name = loc.Name,
            Description = loc.Description,
            IsPrivate = loc.IsPrivate,
            Endpoint = loc.Endpoint,
            Provider = provider,
            LocationType = type,
            IsShared = loc.IsShared,
            Mode = EOperationMode.Editable,
            ServiceName = ApplicationConstants.DefaultServiceNameInDatabase,
            DefaultServiceIntegrationUrl = string.IsNullOrEmpty(loc.DefaultServiceIntegrationUrl)
                ? null
                : loc.DefaultServiceIntegrationUrl.ToNormalizeUrl()
        };
    }

    public static ExtendedProjectModel ConvertToExtendedModel(this Project project)
    {
        ProjectNotFoundException.ThrowIfNull(project);
        return new()
        {
            Name = project.Name,
            ShortName = project.ShortName,
            Description = project.Description,
            ContactEmail = project.Email,
            StartDate = project.StartDate,
            EndDate = project.EndDate,
            DomainId = project.ProjectDomainType.Id,
            AssignedProjectResources = project.AssignedProjectResources.Where(w => !w.IsDeleted && w.ProjectResource?.IsDeleted != true)
                .Select(s => s.ProjectResource).SelectMany(x => x.AssignedLocations)
                .Select(s => new ProjectResourceAssignmentExtendedModel()
                {
                    AllocationAmount = s.AllocationUnitCount,
                    AllocationUnitTypeName = s.AllocationUnitType.Name,
                    AggregationName = s.AggregationName,
                    LocationName = s.Location.Name,
                    LocationTypeId = s.Location.LocationType.Id,
                    ProjectResourceName = s.ProjectResource.Name,
                    Specifications = s.AssignedLocationsValues.Select(s => new KeyValuePair<string, string>(s.Name, s.Value)).ToList()
                })
                .ToList(),
            AssignedTags = project.ProjectTags?.Where(w => !w.IsDeleted).Select(s => s.Id).ToList() ?? []
        };
    }
}