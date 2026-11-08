using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

using UserOrg.Shared.Persistance;
using UserOrg.Shared.Persistance.Abstractions.Interfaces;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

/// <summary>
/// Database context
/// </summary>
public class DatabaseContext : DbContext, IUserOrgDbContext
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="options">Context options</param>
    // ponytail: dropped the logger ctor + its startup migration self-check. Legacy context owns 0
    // migrations (all moved to Core.DataAccess.DatabaseContext, which overlays the shared
    // __EFMigrationsHistory), so GetMigrations() was always empty and the check reported a false
    // "not synchronized" (Latest: none). Migration status is now the modular context's responsibility.
    public DatabaseContext(DbContextOptions<DatabaseContext> options)
        : base(options)
    {

    }
    #endregion
    #region Override Methods
    /// <summary>
    /// OnModelCreating
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DatabaseContext).Assembly);

        modelBuilder.ApplyEnumEntityConfiguration();
        modelBuilder.ApplyAuditableEntityConfiguration();
        modelBuilder.ApplyAuditableServiceIntegrationEntityConfiguration();
        modelBuilder.ApplyAuditableExtendedServiceIntegrationEntityConfiguration();
        modelBuilder.ApplyCodeGeneratedGuidKeys();
    }
    #endregion
    #region Entities
    public virtual DbSet<AllocationUnitType> AllocationUnitTypes { get; set; }
    public virtual DbSet<AssignedLocation> AssignedLocations { get; set; }
    public virtual DbSet<AssignedLocationValue> AssignedLocationValues { get; set; }
    public virtual DbSet<AssignedProjectResourceToProject> AssignedProjectResourceToProjects { get; set; }
    public virtual DbSet<AssignedUserToProject> AssignedUserToProjects { get; set; }
    public virtual DbSet<Configuration> Configurations { get; set; }
    public virtual DbSet<ConfigurationType> ConfigurationTypes { get; set; }
    public virtual DbSet<Country> Countries { get; set; }
    public virtual DbSet<Location> Locations { get; set; }
    public virtual DbSet<LocationType> LocationTypes { get; set; }
    public virtual DbSet<OnboardingRequest> OnboardingRequests { get; set; }
    public virtual DbSet<Project> Projects { get; set; }
    public virtual DbSet<ProjectDomainType> ProjectDomainTypes { get; set; }
    public virtual DbSet<ProjectResource> ProjectResources { get; set; }
    public virtual DbSet<ProjectTag> ProjectTags { get; set; }
    public virtual DbSet<Provider> Providers { get; set; }
    public virtual DbSet<Request> SelfOnboardingRequests { get; set; }
    public virtual DbSet<SystemModule> SystemModules { get; set; }
    public virtual DbSet<UserProjectAccess> UserProjectAccesses { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<UserPermission> UserPermissions { get; set; }
    public virtual DbSet<UserPreference> UserPreferences { get; set; }



    public IQueryable<AllocationUnitType> AllocationUnitTypesQuery => Set<AllocationUnitType>().AsQueryable();
    public IQueryable<AssignedLocation> AssignedLocationsQuery => Set<AssignedLocation>().AsQueryable();
    public IQueryable<AssignedLocationValue> AssignedLocationValuesQuery => Set<AssignedLocationValue>().AsQueryable();
    public IQueryable<AssignedProjectResourceToProject> AssignedProjectResourceToProjectsQuery => Set<AssignedProjectResourceToProject>().AsQueryable();
    public IQueryable<AssignedUserToProject> AssignedUserToProjectsQuery => Set<AssignedUserToProject>().AsQueryable();
    public IQueryable<Configuration> ConfigurationsQuery => Set<Configuration>().AsQueryable();
    public IQueryable<ConfigurationType> ConfigurationTypesQuery => Set<ConfigurationType>().AsQueryable();
    public IQueryable<Country> CountriesQuery => Set<Country>().AsQueryable();
    public IQueryable<Location> LocationsQuery => Set<Location>().AsQueryable();
    public IQueryable<LocationType> LocationTypesQuery => Set<LocationType>().AsQueryable();
    public IQueryable<Project> ProjectsQuery => Set<Project>().AsQueryable();
    public IQueryable<ProjectDomainType> ProjectDomainTypesQuery => Set<ProjectDomainType>().AsQueryable();
    public IQueryable<ProjectResource> ProjectResourcesQuery => Set<ProjectResource>().AsQueryable();
    public IQueryable<ProjectTag> ProjectTagsQuery => Set<ProjectTag>().AsQueryable();
    public IQueryable<Provider> ProvidersQuery => Set<Provider>().AsQueryable();
    public IQueryable<Request> SelfOnboardingRequestsQuery => Set<Request>().AsQueryable();
    public IQueryable<SystemModule> SystemModulesQuery => Set<SystemModule>().AsQueryable();
    public IQueryable<UserProjectAccess> UserProjectAccessesQuery => Set<UserProjectAccess>().AsQueryable();
    public IQueryable<User> UsersQuery => Set<User>().AsQueryable();
    public IQueryable<UserPermission> UserPermissionsQuery => Set<UserPermission>().AsQueryable();
    public IQueryable<UserPreference> UserPreferencesQuery => Set<UserPreference>().AsQueryable();

    void IUserOrgDbContext.Add<T>(T entity) => base.Add(entity);
    void IUserOrgDbContext.Remove<T>(T entity) => base.Remove(entity);
    public void ClearChangeTracker() => ChangeTracker.Clear();
    EntityEntry<T> IUserOrgDbContext.Entry<T>(T entity) => base.Entry(entity);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) =>
        Database.BeginTransactionAsync(ct);

    #endregion

    protected void MarkAsModified<T>(T entity)
    {
        Entry(entity).State = EntityState.Modified;
        Entry(entity).Property("ModifiedAt").CurrentValue = DateTime.UtcNow;
    }

    void IUserOrgDbContext.MarkAsModified<T>(T entity) => MarkAsModified(entity);

    public override int SaveChanges()
    {
        UpdateAuditableEntities();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditableEntities();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateAuditableEntities()
    {
        var entries = ChangeTracker.Entries<IAuditableEntity>();
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
}