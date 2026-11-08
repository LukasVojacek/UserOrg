using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Application.Abstraction;

public interface IUserOrgDbContext
{
    IQueryable<AllocationUnitType> AllocationUnitTypesQuery { get; }
    IQueryable<AssignedLocation> AssignedLocationsQuery { get; }
    IQueryable<AssignedLocationValue> AssignedLocationValuesQuery { get; }
    IQueryable<AssignedProjectResourceToProject> AssignedProjectResourceToProjectsQuery { get; }
    IQueryable<AssignedUserToProject> AssignedUserToProjectsQuery { get; }
    IQueryable<Configuration> ConfigurationsQuery { get; }
    IQueryable<ConfigurationType> ConfigurationTypesQuery { get; }
    IQueryable<Country> CountriesQuery { get; }
    IQueryable<Location> LocationsQuery { get; }
    IQueryable<LocationType> LocationTypesQuery { get; }
    IQueryable<Project> ProjectsQuery { get; }
    IQueryable<ProjectDomainType> ProjectDomainTypesQuery { get; }
    IQueryable<ProjectResource> ProjectResourcesQuery { get; }
    IQueryable<ProjectTag> ProjectTagsQuery { get; }
    IQueryable<Provider> ProvidersQuery { get; }
    IQueryable<Request> SelfOnboardingRequestsQuery { get; }
    IQueryable<SystemModule> SystemModulesQuery { get; }
    IQueryable<UserProjectAccess> UserProjectAccessesQuery { get; }
    IQueryable<User> UsersQuery { get; }
    IQueryable<UserPermission> UserPermissionsQuery { get; }
    IQueryable<UserPreference> UserPreferencesQuery { get; }

    void Add<T>(T entity) where T : class;
    void AddRange(IEnumerable<object> entities);
    void Remove<T>(T entity) where T : class;
    void RemoveRange(IEnumerable<object> entities);
    void ClearChangeTracker();
    EntityEntry<T> Entry<T>(T entity) where T : class;
    void MarkAsModified<T>(T entity);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}