Core.DataAccess — Domain-Driven Design Persistence Layer

## Struktura

- **Contexts/**
  - `IReadUserOrgDbContext` - Read-only queries, flexible for complex scenarios
  - `IWriteUserOrgDbContext` - Write operations, enforces aggregate root boundaries
  - `DatabaseContext` - EF Core DbContext implementation

- **Entities/** - DDD domain models with shadow properties (no public IDs)
  - 4 Aggregate Roots: User, Project, Provider, Request
  - 7 Child entities with ownership semantics
  - All entities have natural identifiers (Email, ProjectShortName, Name, ContactEmail)

- **Configurations/** - EF Core fluent API configuration
  - Shadow properties for ID (Guid)
  - Alternate keys for natural identifiers
  - Relationships and delete behaviors

- **Repositories/** - Data access patterns for aggregates
  - UserRepository, ProjectRepository, ProviderRepository, RequestRepository
  - Query by natural ID + shadow property (Id)

## Key Features

✅ DDD Aggregate Boundaries (Read/Write separation)
✅ Shadow Properties (IDs hidden from domain layer)
✅ Natural Identifiers (Email, ProjectShortName, Name, ContactEmail)
✅ Fluent Configuration (all in Configurations/ folder)
✅ Repository Pattern (per aggregate root)

## Usage

```csharp
// DI Registration in AppHost
services.AddCoreDataAccess(configuration);

// Usage in Application layer
public class UserService
{
    private readonly IUserRepository _users;

    public async Task<User?> GetUserAsync(string email)
    {
        return await _users.GetByEmailAsync(email);
    }
}
```

## ResourceRequests (schema-isolated unit)

The resource-assignment request projection (ADR-0063, ADR-0066) occupies its own schema (`resourceRequests`) with independent EF migrations, mirroring the Outbox and HEAppE integration isolation patterns.

- **Models**
  - `ProjectResourceRequest` — read-model row: state, token, workflow reference, timestamps
  - `ResourceRequestState` — lifecycle value object (PendingPiApproval → AwaitingResourceCreation → terminal)
  - `ResourceRequestSlug` — deterministic slug generation (`{project}-{resource}` with `-2/-3/… suffix for duplicates)

- **Persistence**
  - `ResourceRequestsDbContext` — isolated DbContext for `resourceRequests` schema
  - `ResourceRequestsDbContextDesignTimeFactory` — EF Core tooling integration
  - `ResourceRequestsMigrationStartupService` — startup migration & pending-check

- **Query**
  - `IProjectResourceRequestQuery` — read adapter (list-by-project, find-by-slug, find-by-token, find-awaiting-by-resource)
  - `ProjectResourceRequestQuery` — EF implementation with fluent filters pushed to store

- **Composition**
  - `AddResourceRequestsPersistence()` — DI registration (context factory, migrator, query adapter); called from host B09

## Migrations

To create initial migration:
```bash
dotnet ef migrations add InitialCreate -s src/UserOrg.AppHost -p src/Modules/Core/DataAccess
```

To apply migrations:
```bash
dotnet ef database update -s src/UserOrg.AppHost -p src/Modules/Core/DataAccess
```

## Next Steps

1. ✅ Create Core.DataAccess project structure
2. ✅ Create DDD entities with shadow properties
3. ✅ Create EF configurations with natural IDs
4. ✅ Create context interfaces (Read/Write)
5. ✅ Create DatabaseContext implementation
6. ✅ Create repositories for aggregates
7. ✅ Create DI registration
8. ⏳ Register in AppHost Program.cs
9. ⏳ Create initial EF Core migrations
10. ⏳ Write integration tests
