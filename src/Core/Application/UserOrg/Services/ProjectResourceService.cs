using System.Text;
using System.Text.Json;

using Core.Domain.Exceptions;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain;
using UserOrganizationManagement.Domain.Configuration;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Service;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.Services;

public class ProjectResourceService : IProjectResourceService
{
    private const string emailSubject = "Resource Creation Request";
    private const string htmlEndpoint = "api/Email/Html";
    #region Instances
    /// <summary>
    /// UnitOfWork
    /// </summary>
    private readonly IUserOrgUnitOfWork _unitOfWork;
    private readonly IUserService _userService;
    private readonly IActionLinkService _actionLinkService;
    private readonly ILogger<ProjectResourceService> _logger;
    private readonly IRoleListService _roleListService;
    private readonly HttpClient _emailHttpClient;
    private readonly MailConfiguration _mailConfiguration;
    private readonly string _actionValidHours;
    private readonly HybridCache _cache;

    #endregion
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="userOrgUnitOfWork">UserOrg UnitOfWork</param>
    public ProjectResourceService(IUserOrgUnitOfWork userOrgUnitOfWork, IUserService userService, IActionLinkService actionLinkService, IHttpClientFactory httpClientFactory, ILogger<ProjectResourceService> logger, IOptions<MailConfiguration> mailOptions, IOptions<Domain.Configuration.ActionLink> linkOptions, IRoleListService roleListService, HybridCache cache)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(mailOptions);
        ArgumentNullException.ThrowIfNull(linkOptions);
        _unitOfWork = userOrgUnitOfWork;
        _userService = userService;
        _actionLinkService = actionLinkService;
        _logger = logger;
        _roleListService = roleListService;
        _emailHttpClient = httpClientFactory.CreateClient(Constants.MailSenderHttpClientName);
        _mailConfiguration = mailOptions.Value;
        _actionValidHours = $"{linkOptions.Value.DurationInHours}";
        _cache = cache;
    }
    #endregion
    public async Task<IList<ProjectResourceModel>> GetProjectResourcesAsync(Guid? providerId, ProjectId? projectId, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct = default)
    {
        IList<ProjectResource> projectResource = await _unitOfWork.ProjectResourceRepository.GetAllBySpecificationsAsync(providerId, projectId, statusId, locationTypeId, startDate, endDate, ct);

        if ((projectId is not null || statusId is not null || locationTypeId is not null) && projectResource.Count == 0)
        {
            var project = projectId is null ? string.Empty : $"projectId:{projectId}";
            var status = statusId is null ? string.Empty : $"statusId:{statusId}";
            var locationType = locationTypeId is null ? string.Empty : $"locationTypeId:{locationTypeId}";
            throw new RecordNotFoundException($"""Project Resource for input parameters: {project} {status} {locationType} do not exist!""");
        }

        return projectResource.Select(s => s.ConvertToModel()).ToList();
    }
    public async Task<IList<ProjectResourceModel>> GetProjectResourcesForUserAsync(string userSid, ProjectShortName? projectShortName, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct = default)
    {
        User user = await _userService.GetUserBySidAsync(userSid, ct)
            ?? throw new RecordNotFoundException($"""User with sid:"{userSid}" does not exist!""");

        var userProjects = user.AssignedToProjects.Where(w => !w.IsDeleted)
            .Select(up => up.Project.ShortName)
            .ToList();

        IList<ProjectResource> projectResource = await _unitOfWork.ProjectResourceRepository.GetAllBySpecificationsAsync(null, projectShortName, statusId, locationTypeId, startDate, endDate, ct);
        var filtred = projectResource
            .Where(x => x.AssignedProjects.Any(p => userProjects.Exists(up => p.Project.ShortName == up)))
            .DistinctBy(x => x.Name)
            .Select(s => s.ConvertToModel())
            .ToList();

        return filtred;
    }
    public async Task<IList<ProjectResourceModel>> GetProjectResourcesAsync(Guid? providerId, ProjectShortName? projectShortName, int? statusId, int? locationTypeId, DateTime? startDate, DateTime? endDate, CancellationToken ct = default)
    {
        IList<ProjectResource> projectResource = await _unitOfWork.ProjectResourceRepository.GetAllBySpecificationsAsync(providerId, projectShortName, statusId, locationTypeId, startDate, endDate, ct);

        if ((projectShortName is not null || statusId is not null || locationTypeId is not null) && projectResource.Count == 0)
        {
            var project = projectShortName is null ? string.Empty : $"projectShortName:{projectShortName}";
            var status = statusId is null ? string.Empty : $"statusId:{statusId}";
            var locationType = locationTypeId is null ? string.Empty : $"locationTypeId:{locationTypeId}";
            _logger.LogWarning("Project Resource for input parameters: {Project} {Status} {LocationType} do not exist!", project, status, locationType);
            return [];
        }

        return projectResource.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<ProjectResourceModel> GetProjectResourceAsync(Guid? providerId, Guid id, CancellationToken ct = default)
    {
        ProjectResource projectResource = await _unitOfWork.ProjectResourceRepository.GetByIdWithIncludesAsync(id, ct);
        return projectResource is null || (providerId.HasValue && !projectResource.AssignedLocations.Any(x => x.Location.Provider.Id == providerId.Value))
            ? throw new RecordNotFoundException($"""Project Resource with id:"{id}" does not exist!""")
            : await Task.FromResult(projectResource.ConvertToModel());
    }

    public async Task<IList<ProjectInfoModel>> GetAllProjectResourceWithDetailsAsync(CancellationToken ct = default)
    {
        IList<ProjectResource> projectResource = await _unitOfWork.ProjectResourceRepository.GetAllForServiceAsync(ct);

        DateTime currentDate = DateTime.Today;
        var groupedProjectResources = projectResource.Where(p => p.EndDate >= currentDate)
            .GroupBy(g => g.AssignedLocations.FirstOrDefault()?.Location.Provider.Id)
            .ToList();

        var data = new List<ProjectInfoModel>();
        foreach (IGrouping<Guid?, ProjectResource> providerGroup in groupedProjectResources)
        {
            var resources = providerGroup.ToList();
            Provider provider = resources.FirstOrDefault()?.AssignedLocations?.FirstOrDefault()?.Location?.Provider;

            var projectInfo = new ProjectInfoModel
            {
                ProviderId = providerGroup.Key?.ToString() ?? string.Empty,
                ProviderName = provider?.Name,
                ProviderDescription = provider?.Description ?? string.Empty,
                Resources = resources.Select(s => s.ConvertToModelForService(provider?.Name!)).ToList()

            };
            data.Add(projectInfo);
        }

        return data;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2234:Pass system uri objects instead of strings", Justification = "<Pending>")]
    private async Task SendResourceAddedEmail(Request request, string validForHours, HostString baseUri, CancellationToken ct)
    {
        var requestBody = new
        {
            Sender = _mailConfiguration.SenderEmail,
            Subject = emailSubject,
            Receivers = new string[1] { request.Project.Email },
            TemplateName = "ResourceAdded",
            TemplateArguments = new string[] { request.Id.ToString(), _mailConfiguration.PortalAddress, validForHours },
            Priority = 1
        };
        var serializedRequest = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(serializedRequest, Encoding.Default, "application/json");
        _actionLinkService.GenerateActionLinks(baseUri, request.Id, ct);
        await _emailHttpClient.PostAsync(htmlEndpoint, content, ct);

    }
    public async Task<Guid> InsertProjectResourceAsync(ProjectResourceCreate obj, HostString baseUri, CancellationToken ct = default)
    {
        ProjectResource createdProject = obj.ConvertToEntity();
        await _unitOfWork.ProjectResourceRepository.InsertAsync(createdProject, ct);

        var changes = await _unitOfWork.SaveAsync(ct);
        IList<Request> requests = await _unitOfWork.RequestRepository.GetRequestsForResource(obj.Name, ct);
        if (changes > 0 && requests.Count > 0)
        {
            foreach (Request request in requests)
            {
                request.ProjectResource = createdProject;
                await SendResourceAddedEmail(request, _actionValidHours, baseUri, ct);
            }
        }

        await _unitOfWork.SaveAsync(ct);
        await InvalidateTemplatesInfoForResourceAsync(createdProject.Name, ct);
        return createdProject.Id;
    }

    public async Task UpdateProjectResourceAsync(ProjectResourceUpdate obj, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ProjectResource projectResource = await _unitOfWork.ProjectResourceRepository.GetByIdAsync(obj.Id!.Value, ct)
            ?? throw new RecordNotFoundException($"""Project Resource with id:"{obj.Id}" does not exist!""");
        RecordReadOnlyException.ThrowIfReadOnly(projectResource.Mode);

        UpdateEntity(projectResource, obj);
        await _unitOfWork.SaveAsync(ct);
        await InvalidateTemplatesInfoForResourceAsync(projectResource.Name, ct);
    }

    public async Task DeleteProjectResourceAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        ProjectResource projectResource = await _unitOfWork.ProjectResourceRepository.GetByIdAsync(id, ct).ConfigureAwait(true)
            ?? throw new RecordNotFoundException($"""Project Resource with id:"{id}" does not exist!""");
        RecordReadOnlyException.ThrowIfReadOnly(projectResource.Mode);

        UserPermission systemPermission = await _unitOfWork.UserPermissionRepository.GetSystemPermissionsForUserAsync(userId, ct);

        var roles = await _roleListService.ListRoles(ct);
        if (!roles.IsSuccess)
        {
            throw new Exception("Failed to retrieve roles from Role Service.");
        }
        var platformAdminRole = roles.Value.FirstOrDefault(r => r.Name == RoleInfoConstants.PlatformAdministratorRoleName);
        var providerAdminRole = roles.Value.FirstOrDefault(r => r.Name == RoleInfoConstants.ProviderAdministratorRoleName);
        if (platformAdminRole == null || providerAdminRole == null)
        {
            throw new Exception("Required roles not found in Role Service.");
        }

        var isAdmin = systemPermission.SystemRoleId == platformAdminRole.Id || systemPermission.SystemRoleId == providerAdminRole.Id;
        if (!isAdmin)
        {
            return;
        }

        var affectedProjectShortNames = await GetProjectShortNamesForResourceAsync(projectResource.Name, ct);

        await _unitOfWork.ProjectResourceRepository.DeleteAsync(projectResource.Id, ct);
        await _unitOfWork.SaveAsync(ct);

        foreach (var shortName in affectedProjectShortNames)
        {
            await _cache.RemoveByTagAsync(CacheTags.TemplatesInfo(shortName), ct);
        }
    }

    private async Task InvalidateTemplatesInfoForResourceAsync(string resourceName, CancellationToken ct)
    {
        foreach (var shortName in await GetProjectShortNamesForResourceAsync(resourceName, ct))
        {
            await _cache.RemoveByTagAsync(CacheTags.TemplatesInfo(shortName), ct);
        }
    }

    private async Task<IReadOnlyList<string>> GetProjectShortNamesForResourceAsync(string resourceName, CancellationToken ct)
    {
        var projectsWithResources = await _unitOfWork.ProjectRepository.GetAllWithResources(ct);
        return projectsWithResources
            .Where(p => string.Equals(p.ResourceName, resourceName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ShortName)
            .Distinct()
            .ToList();
    }

    private static void UpdateEntity(ProjectResource projectResource, ProjectResourceUpdate obj)
    {
        projectResource.Name = obj.Name;
        projectResource.Description = obj.Description;
        projectResource.PrimaryInvestigatorEmail = obj.PrimaryInvestigatorEmail!;
        projectResource.StartDate = obj.StartDate;
        projectResource.EndDate = obj.EndDate;
    }

    public async Task<ProjectResourceModel?> GetProjectResourceByNameAsync(string resourceName, CancellationToken ct = default)
    {
        ProjectResource projectResource = await _unitOfWork.ProjectResourceRepository.GetByNameWithIncludesAsync(resourceName, ct);

        if (projectResource == null)
        {
            _logger.LogWarning("Project Resource with name: {ResourceName} does not exist.", resourceName);
            return null;
        }

        return projectResource.ConvertToModel();
    }
}