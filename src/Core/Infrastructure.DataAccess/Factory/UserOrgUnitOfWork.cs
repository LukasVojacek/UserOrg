using Microsoft.EntityFrameworkCore;

using UserOrg.Shared.Persistance.Abstractions.Interfaces;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Factory;

/// <summary>
/// UserOrganization UnitOfWork
/// </summary>
/// <remarks>
/// 
/// </remarks>
/// <param name="context"></param>
/// <param name="allocationUnitTypeRepository"></param>
/// <param name="assignedLocationRepository"></param>
/// <param name="assignedLocationValueRepository"></param>
/// <param name="configurationHistoryRepository"></param>
/// <param name="configurationRepository"></param>
/// <param name="configurationTypeRepository"></param>
/// <param name="countryRepository"></param>
/// <param name="locationRepository"></param>
/// <param name="locationTypeRepository"></param>
/// <param name="projectDomainTypeRepository"></param>
/// <param name="projectTagRepository"></param>
/// <param name="projectHistoryRepository"></param>
/// <param name="projectRepository"></param>
/// <param name="projectResourceHistoryRepository"></param>
/// <param name="projectResourceRepository"></param>
/// <param name="providerRepository"></param>
/// <param name="selfOnboardingRequestRepository"></param>
/// <param name="systemModuleRepository"></param>
/// <param name="systemPermissionTypeRepository"></param>
/// <param name="systemRoleRepository"></param>
/// <param name="userRepository"></param>
/// <param name="userPreferenceRepository"></param>
/// <param name="permissionRepository"></param>
/// <param name="workflowExecutionRepository"></param>
/// <param name="workflowExecutionStatusRepository"></param>
/// <param name="workflowRepository"></param>
/// <param name="workflowTemplateRepository"></param>
/// <param name="workflowTypeRepository"></param>
public class UserOrgUnitOfWork(DatabaseContext context,
                         IAllocationUnitTypeRepository allocationUnitTypeRepository,
                         IAssignedLocationRepository assignedLocationRepository,
                         IAssignedLocationValueRepository assignedLocationValueRepository,
                         IConfigurationRepository configurationRepository,
                         IConfigurationTypeRepository configurationTypeRepository,
                         ICountryRepository countryRepository,
                         ILocationRepository locationRepository,
                         ILocationTypeRepository locationTypeRepository,
                         IProjectDomainTypeRepository projectDomainTypeRepository,
                         IProjectTagRepository projectTagRepository,
                         IProjectRepository projectRepository,
                         IProjectResourceRepository projectResourceRepository,
                         IProviderRepository providerRepository,
                         IRequestRepository selfOnboardingRequestRepository,
                         ISystemModuleRepository systemModuleRepository,
                         IUserRepository userRepository,
                         IUserPreferenceRepository userPreferenceRepository,
                         IUserPermissionRepository permissionRepository,
                         IActionLinkRepository actionLinkRepository,
                         IUserProjectAccessRepository userProjectAccessRepository) : IUserOrgUnitOfWork
{

    #region Instances
    #endregion
    #region Properties
    /// <summary>
    /// Check is disposed
    /// </summary>
    public bool IsDisposed { get; private set; }
    #endregion
    #region Repositories
    public IAllocationUnitTypeRepository AllocationUnitTypeRepository { get; private set; } = allocationUnitTypeRepository;
    public IAssignedLocationRepository AssignedLocationRepository { get; private set; } = assignedLocationRepository;
    public IAssignedLocationValueRepository AssignedLocationValueRepository { get; private set; } = assignedLocationValueRepository;
    public IConfigurationRepository ConfigurationRepository { get; private set; } = configurationRepository;
    public IConfigurationTypeRepository ConfigurationTypeRepository { get; private set; } = configurationTypeRepository;
    public ICountryRepository CountryRepository { get; private set; } = countryRepository;
    public ILocationTypeRepository LocationTypeRepository { get; private set; } = locationTypeRepository;
    public ILocationRepository LocationRepository { get; private set; } = locationRepository;
    public IProjectDomainTypeRepository ProjectDomainTypeRepository { get; private set; } = projectDomainTypeRepository;
    public IProjectTagRepository ProjectTagRepository { get; private set; } = projectTagRepository;
    public IProjectRepository ProjectRepository { get; private set; } = projectRepository;
    public IRequestRepository RequestRepository { get; private set; } = selfOnboardingRequestRepository;
    public IProjectResourceRepository ProjectResourceRepository { get; private set; } = projectResourceRepository;
    public IProviderRepository ProviderRepository { get; private set; } = providerRepository;
    public ISystemModuleRepository SystemModuleRepository { get; private set; } = systemModuleRepository;
    public IUserRepository UserRepository { get; private set; } = userRepository;
    public IUserPermissionRepository UserPermissionRepository { get; private set; } = permissionRepository;
    public IUserPreferenceRepository UserPreferenceRepository { get; private set; } = userPreferenceRepository;

    public IUserProjectAccessRepository UserProjectAccessRepository { get; private set; } = userProjectAccessRepository;
    public IActionLinkRepository ActionLinkRepository { get; } = actionLinkRepository;

    #endregion
    #region Constructors
    #endregion
    #region Interface Methods
    /// <summary>
    /// Save
    /// </summary>
    public void Save()
    {
        UpdateAuditableEntities();
        context.SaveChanges();
    }

    /// <summary>
    /// Save async
    /// </summary>
    public async Task<int> SaveAsync(CancellationToken cancelationToken)
    {
        UpdateAuditableEntities();
        return await context.SaveChangesAsync(cancelationToken);
    }

    /// <summary>
    /// Dispose
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose
    /// </summary>
    /// <param name="disposing">Is disposing</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed && disposing)
        {
            context.Dispose();
        }

        IsDisposed = true;
    }
    #endregion
    #region Private Methods
    /// <summary>
    /// Update auditable entities
    /// </summary>
    private void UpdateAuditableEntities()
    {
        var entries = context.ChangeTracker.Entries<IAuditableEntity>();
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property<DateTime>("CreatedAt").CurrentValue = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Property<DateTime?>("ModifiedAt").CurrentValue = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Deleted)
            {
                entry.Property<bool>("IsDeleted").CurrentValue = true;
                entry.Property<DateTime?>("ModifiedAt").CurrentValue = DateTime.UtcNow;
                entry.State = EntityState.Modified;
            }
        }
    }
    #endregion
}