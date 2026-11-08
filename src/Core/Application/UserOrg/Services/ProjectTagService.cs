using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectTag;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectTag;

namespace UserOrganizationManagement.Application.UserOrg.Services;

/// <summary>
/// Constructor
/// </summary>
/// <param name="userOrgUnitOfWork">UserOrg UnitOfWork</param>
public class ProjectTagService(IUserOrgUnitOfWork userOrgUnitOfWork) : IProjectTagService
{

    #region Constructors
    #endregion
    public async Task<IList<ProjectTagModel>> GetProjectTagsAsync(string? tagName, CancellationToken ct)
    {
        IList<ProjectTag> projectTags = await userOrgUnitOfWork.ProjectTagRepository.GetAllByTagNameAsync(tagName, ct);
        return tagName is not null && projectTags.Count == 0
            ? throw new RecordNotFoundException($"""ProjectTags for input parameter: tagName:"{tagName}" do not exist!""")
            : (IList<ProjectTagModel>)projectTags.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<ProjectTagModel> GetProjectTagAsync(Guid id, CancellationToken ct)
    {
        ProjectTag projectTag = await userOrgUnitOfWork.ProjectTagRepository.GetByIdAsync(id, ct);
        return projectTag is null
                ? throw new RecordNotFoundException($"""ProjectTag with id:"{id}" does not exist!""")
                : await Task.FromResult(projectTag.ConvertToModel());
    }

    public async Task<Guid> InsertProjectTagAsync(ProjectTagCreate obj, Guid createdByUserId, CancellationToken ct)
    {
        User createdByUser = await userOrgUnitOfWork.UserRepository.GetByIdAsync(createdByUserId, ct)
            ?? throw new RecordNotFoundException($"""User with id:"{createdByUserId}" does not exist!""");

        ProjectTag createdTag = obj.ConvertToEntity(createdByUser);
        await userOrgUnitOfWork.ProjectTagRepository.InsertAsync(createdTag, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
        return createdTag.Id;
    }

    public async Task UpdateProjectTagAsync(ProjectTagUpdate obj, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(obj);
        ProjectTag projectTag = await userOrgUnitOfWork.ProjectTagRepository.GetByIdAsync(obj.Id!.Value, ct)
            ?? throw new RecordNotFoundException($"""ProjectTag with id:"{obj.Id}" does not exist!""");

        UpdateEntity(projectTag, obj);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    public async Task DeleteProjectTagAsync(Guid id, CancellationToken ct)
    {
        ProjectTag projectTag = await userOrgUnitOfWork.ProjectTagRepository.GetByIdAsync(id, ct)
            ?? throw new RecordNotFoundException($"""ProjectTag with id:"{id}" does not exist!""");

        await userOrgUnitOfWork.ProjectTagRepository.DeleteAsync(projectTag.Id, ct);
        await userOrgUnitOfWork.SaveAsync(ct);
    }

    private static void UpdateEntity(ProjectTag projTag, ProjectTagUpdate obj)
    {
        projTag.Name = obj.Name;
        projTag.Description = obj.Description;
    }
}