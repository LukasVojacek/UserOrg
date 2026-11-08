# Phase 2: DbContext Refactoring (Implementation Guide)

**Status:** PENDING (Awaits Phase 1 completion)  
**Objective:** Separate single DatabaseContext into IReadUserOrgDbContext and IWriteUserOrgDbContext  
**Deliverables:** New context interfaces, updated implementation, DI configuration changes

---

## 2.1 Current DbContext Structure

### Existing Architecture

**File:** `src/Core/Infrastructure.DataAccess/Contexts/DatabaseContext.cs`

**Current Situation:**
- Single `DatabaseContext` class inherits from `DbContext`
- Implements `IUserOrgDbContext` interface
- All 24+ entities are queryable through public `DbSet<T>` properties
- Both read and write operations happen through the same context
- No separation of concerns between queries and mutations

```csharp
// CURRENT (Single Unified Context)
public class DatabaseContext : DbContext, IUserOrgDbContext
{
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<Project> Projects { get; set; }
    public virtual DbSet<ProjectTag> ProjectTags { get; set; }
    public virtual DbSet<UserPermission> UserPermissions { get; set; }
    // ... all 24+ entities
    
    public IQueryable<User> UsersQuery => Set<User>().AsQueryable();
    public IQueryable<ProjectTag> ProjectTagsQuery => Set<ProjectTag>().AsQueryable();
    // ... all query properties
    
    public void Add<T>(T entity) where T : class => base.Add(entity);
    public async Task<int> SaveChangesAsync(CancellationToken ct) => 
        await base.SaveChangesAsync(ct);
}

// Problem: Services don't know whether they're reading or writing
public class ProjectService
{
    public ProjectService(IUserOrgDbContext context)
    {
        _context = context;  // Could be read-only or write-only
    }
    
    public async Task<Project> GetProjectAsync(ProjectShortName shortName)
    {
        return await _context.ProjectsQuery.FirstOrDefaultAsync();  // OK
    }
    
    public async Task UpdateProjectAsync(Project project)
    {
        _context.Add(project);  // OK, but context doesn't enforce this is write-only
        await _context.SaveChangesAsync();
    }
}
```

---

## 2.2 New DbContext Architecture

### Separated Read/Write Interfaces

```
┌─────────────────────────────────────────┐
│         DatabaseContext (impl)          │
│  - Implements all entity properties     │
│  - Handles all EF configuration         │
└──────────────┬──────────────────────────┘
               │
    ┌──────────┴──────────┐
    │                     │
    ▼                     ▼
IReadUserOrgDbContext  IWriteUserOrgDbContext
(All entities)         (Aggregates only)
    │                     │
    └──────────┬──────────┘
               │
            ▼
    IUserOrgDbContext (inherits both)
    (Backward compatibility)
```

---

## 2.3 Interface Definitions

### IReadUserOrgDbContext - All Entities Queryable

**Purpose:** Flexible reads, complex queries, reporting, aggregations

**File to create:** `src/Core/Application/Abstraction/IReadUserOrgDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Application.Abstraction;

/// <summary>
/// Read-only DbContext interface providing access to all entities for queries.
/// Used for flexible reads, reports, and complex projections.
/// </summary>
public interface IReadUserOrgDbContext
{
    // Reference Data
    IQueryable<AllocationUnitType> AllocationUnitTypesQuery { get; }
    IQueryable<Country> CountriesQuery { get; }
    IQueryable<Location> LocationsQuery { get; }
    IQueryable<LocationType> LocationTypesQuery { get; }
    IQueryable<Configuration> ConfigurationsQuery { get; }
    IQueryable<ConfigurationType> ConfigurationTypesQuery { get; }
    IQueryable<ConfigurationValue> ConfigurationValuesQuery { get; }
    IQueryable<SystemModule> SystemModulesQuery { get; }
    IQueryable<ProjectDomainType> ProjectDomainTypesQuery { get; }
    
    // Aggregate Roots
    IQueryable<User> UsersQuery { get; }
    IQueryable<Project> ProjectsQuery { get; }
    IQueryable<Provider> ProvidersQuery { get; }
    IQueryable<Request> SelfOnboardingRequestsQuery { get; }
    
    // Child Entities (queryable for complex queries)
    IQueryable<UserPermission> UserPermissionsQuery { get; }
    IQueryable<UserPreference> UserPreferencesQuery { get; }
    IQueryable<ProjectTag> ProjectTagsQuery { get; }
    IQueryable<AssignedUserToProject> AssignedUserToProjectsQuery { get; }
    IQueryable<AssignedProjectResourceToProject> AssignedProjectResourceToProjectsQuery { get; }
    IQueryable<ProjectResource> ProjectResourcesQuery { get; }
    IQueryable<UserProjectAccess> UserProjectAccessesQuery { get; }
    
    // Additional entities needing review
    IQueryable<AssignedLocation> AssignedLocationsQuery { get; }
    IQueryable<AssignedLocationValue> AssignedLocationValuesQuery { get; }
    IQueryable<ActionLink> ActionLinksQuery { get; }
}
```

---

### IWriteUserOrgDbContext - Aggregates Only + Write Operations

**Purpose:** Enforce DDD boundaries - only aggregate roots queryable for updates

**File to create:** `src/Core/Application/Abstraction/IWriteUserOrgDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Application.Abstraction;

/// <summary>
/// Write DbContext interface providing access only to aggregate roots and write operations.
/// Enforces DDD boundaries: only aggregates are queryable for modifications.
/// Child entities must be accessed through their parent aggregate.
/// </summary>
public interface IWriteUserOrgDbContext
{
    // ONLY Aggregate Roots queryable for writes
    IQueryable<User> UsersQuery { get; }
    IQueryable<Project> ProjectsQuery { get; }
    IQueryable<Provider> ProvidersQuery { get; }
    IQueryable<Request> SelfOnboardingRequestsQuery { get; }
    
    // Write Operations
    void Add<T>(T entity) where T : class;
    void MarkAsModified<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    // Change tracking management (advanced)
    void ClearChangeTracker();
    EntityEntry<T> Entry<T>(T entity) where T : class;
}
```

---

### IUserOrgDbContext - Unified Interface (Backward Compatibility)

**File to update:** `src/Core/Application/Abstraction/IUserOrgDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Application.Abstraction;

/// <summary>
/// Unified DbContext interface combining both read and write capabilities.
/// Kept for backward compatibility during migration.
/// 
/// For NEW code, use:
/// - IReadUserOrgDbContext for queries
/// - IWriteUserOrgDbContext for writes
/// </summary>
[Obsolete("Use IReadUserOrgDbContext or IWriteUserOrgDbContext instead. This interface is kept for backward compatibility.")]
public interface IUserOrgDbContext : IReadUserOrgDbContext, IWriteUserOrgDbContext
{
    // Inherits all properties from both interfaces
}
```

---

## 2.4 DatabaseContext Implementation

### Updated DatabaseContext Class

**File to update:** `src/Core/Infrastructure.DataAccess/Contexts/DatabaseContext.cs`

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Persistance;
using UserOrg.Shared.Persistance.Abstractions.Interfaces;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.LogMessages;
using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

/// <summary>
/// Database context implementing both read and write interfaces.
/// Provides all entity properties but separation is enforced at the interface level.
/// </summary>
public class DatabaseContext : DbContext, IUserOrgDbContext
{
    #region Static Instances
    private static readonly object _lockObject = new();
    public static bool IsMigrated { get; private set; }
    private readonly ILogger<DatabaseContext>? _logger;
    #endregion

    #region Constructors
    public DatabaseContext(DbContextOptions<DatabaseContext> options)
        : base(options)
    {
    }

    public DatabaseContext(ILogger<DatabaseContext> logger, DbContextOptions<DatabaseContext> options)
        : base(options)
    {
        _logger = logger;
        
        if (!IsMigrated)
        {
            lock (_lockObject)
            {
                if (!IsMigrated)
                {
                    try
                    {
                        if (!Database.CanConnect())
                        {
                            _logger?.LogCritical(
                                "Database connection failed or database does not exist. " +
                                "Please run the DatabaseMigrations tool to create and apply migrations.");
                            IsMigrated = true;
                            return;
                        }

                        if (Database.IsRelational())
                        {
                            var lastAppliedMigration = Database.GetAppliedMigrations().LastOrDefault();
                            var lastDefinedMigration = Database.GetMigrations().LastOrDefault();

                            if (lastAppliedMigration is null)
                            {
                                _logger?.LogCritical(
                                    "Database exists but no migrations have been applied. " +
                                    "Please run the DatabaseMigrations tool with 'apply' mode to initialize the database.");
                                IsMigrated = true;
                                return;
                            }

                            if (lastAppliedMigration != lastDefinedMigration)
                            {
                                _logger?.LogCritical(
                                    "Application and database migrations are not synchronized. " +
                                    "Applied: {AppliedMigration}, Latest: {LatestMigration}. " +
                                    "Please run the DatabaseMigrations tool with 'apply' mode to update the database.",
                                    lastAppliedMigration ?? "none",
                                    lastDefinedMigration ?? "none");
                                IsMigrated = true;
                                return;
                            }

                            _logger?.ValidMigration();
                            IsMigrated = true;
                        }
                    }
                    catch (SqlException ex)
                    {
                        _logger?.DbConnectionProblem(ex.Message);
                        throw;
                    }
                }
            }
        }
    }
    #endregion

    #region Entity Properties (IReadUserOrgDbContext)
    // Reference Data
    public virtual DbSet<AllocationUnitType> AllocationUnitTypes { get; set; } = null!;
    public virtual DbSet<Country> Countries { get; set; } = null!;
    public virtual DbSet<Location> Locations { get; set; } = null!;
    public virtual DbSet<LocationType> LocationTypes { get; set; } = null!;
    public virtual DbSet<Configuration> Configurations { get; set; } = null!;
    public virtual DbSet<ConfigurationType> ConfigurationTypes { get; set; } = null!;
    public virtual DbSet<ConfigurationValue> ConfigurationValues { get; set; } = null!;
    public virtual DbSet<SystemModule> SystemModules { get; set; } = null!;
    public virtual DbSet<ProjectDomainType> ProjectDomainTypes { get; set; } = null!;
    
    // Aggregate Roots
    public virtual DbSet<User> Users { get; set; } = null!;
    public virtual DbSet<Project> Projects { get; set; } = null!;
    public virtual DbSet<Provider> Providers { get; set; } = null!;
    public virtual DbSet<Request> SelfOnboardingRequests { get; set; } = null!;
    
    // Child Entities
    public virtual DbSet<UserPermission> UserPermissions { get; set; } = null!;
    public virtual DbSet<UserPreference> UserPreferences { get; set; } = null!;
    public virtual DbSet<ProjectTag> ProjectTags { get; set; } = null!;
    public virtual DbSet<AssignedUserToProject> AssignedUserToProjects { get; set; } = null!;
    public virtual DbSet<AssignedProjectResourceToProject> AssignedProjectResourceToProjects { get; set; } = null!;
    public virtual DbSet<ProjectResource> ProjectResources { get; set; } = null!;
    public virtual DbSet<UserProjectAccess> UserProjectAccesses { get; set; } = null!;
    public virtual DbSet<AssignedLocation> AssignedLocations { get; set; } = null!;
    public virtual DbSet<AssignedLocationValue> AssignedLocationValues { get; set; } = null!;
    public virtual DbSet<ActionLink> ActionLinks { get; set; } = null!;
    #endregion

    #region Query Properties (IReadUserOrgDbContext)
    // Reference Data Queries
    public IQueryable<AllocationUnitType> AllocationUnitTypesQuery => Set<AllocationUnitType>().AsQueryable();
    public IQueryable<Country> CountriesQuery => Set<Country>().AsQueryable();
    public IQueryable<Location> LocationsQuery => Set<Location>().AsQueryable();
    public IQueryable<LocationType> LocationTypesQuery => Set<LocationType>().AsQueryable();
    public IQueryable<Configuration> ConfigurationsQuery => Set<Configuration>().AsQueryable();
    public IQueryable<ConfigurationType> ConfigurationTypesQuery => Set<ConfigurationType>().AsQueryable();
    public IQueryable<ConfigurationValue> ConfigurationValuesQuery => Set<ConfigurationValue>().AsQueryable();
    public IQueryable<SystemModule> SystemModulesQuery => Set<SystemModule>().AsQueryable();
    public IQueryable<ProjectDomainType> ProjectDomainTypesQuery => Set<ProjectDomainType>().AsQueryable();
    
    // Aggregate Root Queries (IReadUserOrgDbContext & IWriteUserOrgDbContext)
    public IQueryable<User> UsersQuery => Set<User>().AsQueryable();
    public IQueryable<Project> ProjectsQuery => Set<Project>().AsQueryable();
    public IQueryable<Provider> ProvidersQuery => Set<Provider>().AsQueryable();
    public IQueryable<Request> SelfOnboardingRequestsQuery => Set<Request>().AsQueryable();
    
    // Child Entity Queries (IReadUserOrgDbContext only)
    public IQueryable<UserPermission> UserPermissionsQuery => Set<UserPermission>().AsQueryable();
    public IQueryable<UserPreference> UserPreferencesQuery => Set<UserPreference>().AsQueryable();
    public IQueryable<ProjectTag> ProjectTagsQuery => Set<ProjectTag>().AsQueryable();
    public IQueryable<AssignedUserToProject> AssignedUserToProjectsQuery => Set<AssignedUserToProject>().AsQueryable();
    public IQueryable<AssignedProjectResourceToProject> AssignedProjectResourceToProjectsQuery => Set<AssignedProjectResourceToProject>().AsQueryable();
    public IQueryable<ProjectResource> ProjectResourcesQuery => Set<ProjectResource>().AsQueryable();
    public IQueryable<UserProjectAccess> UserProjectAccessesQuery => Set<UserProjectAccess>().AsQueryable();
    public IQueryable<AssignedLocation> AssignedLocationsQuery => Set<AssignedLocation>().AsQueryable();
    public IQueryable<AssignedLocationValue> AssignedLocationValuesQuery => Set<AssignedLocationValue>().AsQueryable();
    public IQueryable<ActionLink> ActionLinksQuery => Set<ActionLink>().AsQueryable();
    #endregion

    #region Override Methods
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DatabaseContext).Assembly);

        modelBuilder.ApplyEnumEntityConfiguration();
        modelBuilder.ApplyAuditableEntityConfiguration();
        modelBuilder.ApplyAuditableServiceIntegrationEntityConfiguration();
        modelBuilder.ApplyAuditableExtendedServiceIntegrationEntityConfiguration();
    }
    #endregion

    #region Write Operations (IWriteUserOrgDbContext)
    void IWriteUserOrgDbContext.Add<T>(T entity) => base.Add(entity);
    void IWriteUserOrgDbContext.MarkAsModified<T>(T entity) => MarkAsModified(entity);
    Task<IDbContextTransaction> IWriteUserOrgDbContext.BeginTransactionAsync(CancellationToken ct) =>
        Database.BeginTransactionAsync(ct);
    void IWriteUserOrgDbContext.ClearChangeTracker() => ChangeTracker.Clear();
    EntityEntry<T> IWriteUserOrgDbContext.Entry<T>(T entity) => base.Entry(entity);

    protected void MarkAsModified<T>(T entity)
    {
        Entry(entity).State = EntityState.Modified;
        Entry(entity).Property("ModifiedAt").CurrentValue = DateTime.UtcNow;
    }

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
    #endregion
}
```

---

## 2.5 Dependency Injection Configuration

### Update DI in ServiceDefaults or Program.cs

**File to update:** `src/UserOrg.ServiceDefaults/Extensions.cs` or `src/Core/API/Program.cs`

```csharp
// Example using ServiceDefaults
namespace UserOrg;

public static class ServiceDefaults
{
    public static IServiceCollection AddUserOrgCore(this IServiceCollection services)
    {
        // Register DatabaseContext
        services.AddDbContext<DatabaseContext>(options =>
            options.UseSqlServer(connectionString));
        
        // Register both read and write interfaces pointing to same DatabaseContext instance
        services.AddScoped<IReadUserOrgDbContext>(provider =>
            provider.GetRequiredService<DatabaseContext>());
        
        services.AddScoped<IWriteUserOrgDbContext>(provider =>
            provider.GetRequiredService<DatabaseContext>());
        
        // Keep old interface for backward compatibility (marked as [Obsolete])
        services.AddScoped<IUserOrgDbContext>(provider =>
            provider.GetRequiredService<DatabaseContext>());
        
        // Register repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IProviderRepository, ProviderRepository>();
        // ... other repositories
        
        return services;
    }
}
```

---

## 2.6 Service Injection Pattern Changes

### Before - Services didn't know if read or write

```csharp
public class ProjectService
{
    private readonly IUserOrgDbContext _context;
    
    public ProjectService(IUserOrgDbContext context)
    {
        _context = context;  // Mixed read/write capability
    }
    
    public async Task<ProjectModel> GetProjectAsync(ProjectShortName shortName)
    {
        var project = await _context.ProjectsQuery
            .FirstOrDefaultAsync(p => p.ShortName == shortName);
        return ProjectModel.FromDomain(project);
    }
    
    public async Task UpdateProjectAsync(Project project)
    {
        _context.Add(project);
        await _context.SaveChangesAsync();  // No enforcement
    }
}
```

### After - Clear separation of concerns

```csharp
public class ProjectService
{
    private readonly IReadUserOrgDbContext _readContext;
    private readonly IWriteUserOrgDbContext _writeContext;
    
    public ProjectService(
        IReadUserOrgDbContext readContext,
        IWriteUserOrgDbContext writeContext)
    {
        _readContext = readContext;    // Read-only guarantee
        _writeContext = writeContext;  // Write-only guarantee
    }
    
    public async Task<ProjectModel> GetProjectAsync(ProjectShortName shortName)
    {
        // Can only query, never modify
        var project = await _readContext.ProjectsQuery
            .FirstOrDefaultAsync(p => p.ShortName == shortName);
        return ProjectModel.FromDomain(project);
    }
    
    public async Task UpdateProjectAsync(Project project)
    {
        // Explicitly using write context
        _writeContext.Add(project);
        await _writeContext.SaveChangesAsync();  // Clearly a write operation
    }
}
```

---

## 2.7 Complex Query Service (Read Context)

### Use IReadUserOrgDbContext for Complex Queries

```csharp
/// <summary>
/// Service for complex queries across multiple entities.
/// Uses only IReadUserOrgDbContext - never modifies data.
/// </summary>
public class ProjectReportService
{
    private readonly IReadUserOrgDbContext _readContext;
    
    public ProjectReportService(IReadUserOrgDbContext readContext)
    {
        _readContext = readContext;  // Read-only context
    }
    
    /// <summary>
    /// Get projects with user counts
    /// </summary>
    public async Task<List<ProjectReportDto>> GetProjectsWithUserCountAsync()
    {
        var report = await (from project in _readContext.ProjectsQuery
            join assignments in _readContext.AssignedUserToProjectsQuery
                on project.Id equals assignments.ProjectId
            group assignments by project.ShortName into grp
            select new ProjectReportDto
            {
                ProjectShortName = grp.Key,
                UserCount = grp.Count()
            }).ToListAsync();
        
        return report;
    }
    
    /// <summary>
    /// Get user permissions across projects
    /// </summary>
    public async Task<UserPermissionReportDto> GetUserPermissionsAsync(string email)
    {
        var permissions = await _readContext.UserPermissionsQuery
            .Where(up => up.User.Email == email)
            .GroupBy(up => up.PermissionName)
            .Select(g => new { Permission = g.Key, Count = g.Count() })
            .ToListAsync();
        
        return new UserPermissionReportDto { Permissions = permissions };
    }
}
```

---

## 2.8 Migration Checklist

- [ ] Create `IReadUserOrgDbContext.cs` with all entity queries
- [ ] Create `IWriteUserOrgDbContext.cs` with aggregate root queries + write operations
- [ ] Update `IUserOrgDbContext.cs` to inherit from both (mark as obsolete)
- [ ] Update `DatabaseContext.cs` to implement both interfaces
- [ ] Verify all DbSet properties are still public
- [ ] Verify all query properties are still public
- [ ] Update DI registration in ServiceDefaults/Program.cs
- [ ] Update all service constructors to inject both contexts (or specific one)
- [ ] Verify compilation - should be no breaking changes yet (just additions)
- [ ] Run existing tests - should pass
- [ ] Update integration tests to verify interface separation

---

## 2.9 Benefits of This Approach

| Benefit | Impact |
|---------|--------|
| Clear Intent | Services declare whether they read or write |
| Enforced Boundaries | Write context cannot query child entities directly |
| Testability | Can mock separate read/write contexts |
| Performance | Read queries don't trigger change tracking |
| Security | Write context limited to aggregates only |
| Maintainability | Clear data flow in codebase |

---

## 2.10 Next Steps

After Phase 2 completion:
- ✅ DatabaseContext properly separated
- ✅ DI configured with both interfaces
- ✅ Services can inject read and write separately
- ➡️ **Next: Phase 3 - Remove public Id properties from entities**

---

## References

- **[📖 DDD Migration Documentation Index](./README.md)** - Complete reading guide and navigation
- **[Phase 1: Architecture Analysis](./ddd-migration-phase1-analysis.md)** - Understanding aggregate roots
- **[Phase 3: Remove Public IDs](./ddd-migration-phase3-entities.md)** - Next phase: Shadow properties
- **[EF Core DbContext](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbcontext)** - Microsoft documentation
- **[DDD Bounded Contexts](https://martinfowler.com/bliki/BoundedContext.html)** - Martin Fowler
