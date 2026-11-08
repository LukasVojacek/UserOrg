using UserOrganizationManagement.Domain.IRepositories;

namespace UserOrganizationManagement.Application.Abstraction;

/// <summary>
/// Interface for UserOrganization UnitOfWork
/// </summary>
public interface IUserOrgUnitOfWork : IDisposable
{
    #region Properties
    bool IsDisposed { get; }
    #endregion
    #region Repositories

    IAllocationUnitTypeRepository AllocationUnitTypeRepository { get; }
    IAssignedLocationRepository AssignedLocationRepository { get; }
    IAssignedLocationValueRepository AssignedLocationValueRepository { get; }
    IConfigurationRepository ConfigurationRepository { get; }
    IConfigurationTypeRepository ConfigurationTypeRepository { get; }
    ICountryRepository CountryRepository { get; }
    ILocationTypeRepository LocationTypeRepository { get; }
    ILocationRepository LocationRepository { get; }
    IProjectDomainTypeRepository ProjectDomainTypeRepository { get; }
    IProjectTagRepository ProjectTagRepository { get; }
    IProjectRepository ProjectRepository { get; }
    IRequestRepository RequestRepository { get; }
    IProjectResourceRepository ProjectResourceRepository { get; }
    IProviderRepository ProviderRepository { get; }
    ISystemModuleRepository SystemModuleRepository { get; }
    IUserRepository UserRepository { get; }
    IUserPermissionRepository UserPermissionRepository { get; }
    IUserPreferenceRepository UserPreferenceRepository { get; }
    IUserProjectAccessRepository UserProjectAccessRepository { get; }
    IActionLinkRepository ActionLinkRepository { get; }
    #endregion
    #region Methods
    void Save();
    Task<int> SaveAsync(CancellationToken cancelationToken);
    #endregion
}