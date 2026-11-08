# Core API Database Architecture

Entity Framework Core configuration, entity relationships, repositories, and migration strategy.

## Overview

The Core API uses Entity Framework Core with SQL Server for persistence.

**Key Components:**
- `UserOrgDbContext` — Main DbContext
- 24+ domain entities
- 20+ repositories
- Migration strategy for schema evolution

**Location:** `src/Core/Infrastructure.DataAccess/`

## Database Schema

### Aggregate Roots

Four aggregate root entities that own child entities:

| Aggregate | Natural ID | Child Entities | Purpose |
|-----------|-----------|---|---|
| **User** | Email | UserPermission, UserPreference, AssignedUserToProject | User accounts and preferences |
| **Project** | ProjectShortName | ProjectTag, UserPermission, AssignedUserToProject, AssignedProjectResourceToProject | Research projects |
| **Provider** | Name | ProjectResource, Location, Country (ref) | Service providers |
| **Request** | ContactEmail/UUID | ActionLink, User (ref), Project (ref), Provider (ref) | Access requests |

### Reference Entities

Values that don't own other entities:
- Country
- AllocationUnitType
- SystemModule
- And others

### Mapping Entities

Join tables for many-to-many relationships:
- AssignedUserToProject — User-to-Project assignments
- AssignedProjectResourceToProject — Resource-to-Project assignments
- AssignedLocation — Location assignments

## Entity Framework Configuration

### UserOrgDbContext

Main context that manages all entities:

```csharp
public class UserOrgDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<Provider> Providers { get; set; }
    // ... 21 more DbSets
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Entity configurations
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
    }
}
```

### Entity Configurations

Each entity has a configuration in `Persistence/Configurations/`:

```csharp
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasMany(u => u.UserPermissions)
            .WithOne(up => up.User)
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

## Repository Pattern

### Generic Repository

```csharp
public interface IRepository<T> where T : class
{
    Task<T> GetByIdAsync(object id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    Task<T> UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task<bool> ExistsAsync(object id);
}
```

### Specific Repositories

For each aggregate root:
- `IUserRepository`
- `IProjectRepository`
- `IProviderRepository`
- `IRequestRepository`

**Example:**
```csharp
public interface IProjectRepository : IRepository<Project>
{
    Task<Project> GetByShortNameAsync(string shortName);
    Task<IEnumerable<Project>> GetProjectsByUserAsync(string userId);
    Task<bool> ProjectNameExistsAsync(string shortName);
}
```

## Migrations

### Creating Migrations

```bash
cd src/Core/Infrastructure.DataAccess
dotnet ef migrations add AddNewFeature -o Persistence/Migrations -c UserOrgDbContext
```

### Applying Migrations

Migrations run automatically on startup via:

```csharp
public class MigrationHostedService : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<UserOrgDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
```

### Migration History

Migrations are stored in `Persistence/Migrations/` directory.

Recent migrations:
- AddInitialSchema.cs
- AddUserPreferences.cs
- AddProjectTags.cs
- And others...

## Entity Relationships

### One-to-Many

```csharp
// User has many UserPermissions
builder.HasOne(u => u.User)
    .WithMany(up => up.UserPermissions)
    .HasForeignKey(up => up.UserId);
```

### Many-to-Many

```csharp
// User-to-Project (via join table)
builder.HasMany(u => u.Projects)
    .WithMany(p => p.Users)
    .UsingEntity<AssignedUserToProject>(
        j => j.HasOne(apt => apt.Project).WithMany(),
        j => j.HasOne(apt => apt.User).WithMany()
    );
```

### Self-Referential

```csharp
// Location has parent Location
builder.HasOne(l => l.ParentLocation)
    .WithMany(l => l.ChildLocations)
    .HasForeignKey(l => l.ParentLocationId);
```

## Performance Optimization

### Indexing

```csharp
builder.HasIndex(u => u.Email).IsUnique();
builder.HasIndex(p => p.ProjectShortName).IsUnique();
builder.HasIndex(u => u.Sid).IsUnique();
```

### Query Optimization

```csharp
// Include related entities
var project = await context.Projects
    .Include(p => p.Users)
    .Include(p => p.Resources)
    .FirstOrDefaultAsync(p => p.ProjectShortName == shortName);
```

### Lazy Loading

Lazy loading is enabled for navigation properties:

```csharp
var project = await repository.GetByIdAsync(id);
var users = project.Users; // Lazy loaded
```

## Unit of Work Pattern

Transactions are managed via DbContext:

```csharp
using var transaction = await context.Database.BeginTransactionAsync();
try
{
    // Multiple operations
    await context.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
}
```

## Database Backup & Restore

See [Database Guide](../guides/database.md) for backup and restore procedures.

## Troubleshooting

### Migration Conflicts
- Check pending migrations: `dotnet ef migrations list`
- Reset database: `dotnet ef database drop --force` + `dotnet ef database update`

### Stale Contexts
- Use `AsNoTracking()` for read-only queries
- Detach/reattach entities for complex scenarios

### Performance Issues
- Check query execution time
- Review indexes and foreign keys
- Use query profiling tools

## Status

✅ Fully implemented and production-ready

- 24+ domain entities
- Comprehensive relationships
- Migration strategy
- Repository pattern
- Performance optimized
