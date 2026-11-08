# Core API Services

Application services that implement business logic for managing projects, users, providers, locations, and integrations.

## Service Architecture

All services follow the same pattern:
1. Interface definition in `IServices/`
2. Implementation in `Services/`
3. Dependency injection registration
4. Comprehensive logging and error handling

## User Management

### IUserService
```csharp
Task<User> GetUserByIdAsync(string idpUserId);
Task<User> GetUserByUsernameAsync(string username);
Task<User> GetUserBySidAsync(string sid);
Task<User> CreateUserAsync(User user);
Task<User> UpdateUserAsync(User user);
Task<Dictionary<string, object>> PopulateUserInfoAsync(string idpUserId);
Task<Dictionary<string, object>> PopulateUserInfoExtendedAsync(string idpUserId);
Task GetUserProjectRolesAsync(string userId, string projectShortName);
```

**Responsibilities:**
- User CRUD operations
- User info population
- User preferences management
- Project role queries

## Project Management

### IProjectService
```csharp
Task<IEnumerable<Project>> GetProjectsAsync();
Task<Project> GetProjectAsync(string projectShortName);
Task<Project> CreateProjectAsync(ProjectCreate request);
Task<Project> UpdateProjectAsync(Project project);
Task DeleteProjectAsync(string projectShortName);
Task AssignUserToProjectAsync(string projectShortName, string idpUserId, string roleId);
Task RemoveUserFromProjectAsync(string projectShortName, string idpUserId);
Task AssignResourceToProjectAsync(string projectShortName, string resourceName);
Task RemoveResourceFromProjectAsync(string projectShortName, string resourceName);
Task AssignTagToProjectAsync(string projectShortName, string tagId);
Task RemoveTagFromProjectAsync(string projectShortName, string tagId);
Task<TemplateInfo> GetProjectTemplateInfoAsync(string projectShortName);
Task<ExtendedProjectModel> GetExtendedProjectInfoAsync();
```

**Responsibilities:**
- Project CRUD operations
- User-project assignments
- Resource assignments
- Project tags
- Template information
- Extended project data

## Resource Management

### IProjectResourceService
```csharp
Task<IEnumerable<ProjectResource>> GetResourcesAsync();
Task<ProjectResource> GetResourceAsync(string resourceName);
Task<ProjectResource> CreateResourceAsync(ProjectResourceCreate request);
Task<ProjectResource> UpdateResourceAsync(ProjectResource resource);
Task DeleteResourceAsync(string resourceName);
```

### IProjectResourceAssignmentService
```csharp
Task<ProjectResourceAssignment> CreateAssignmentAsync(ProjectResourceAssignmentCreate request);
Task<ProjectResourceAssignment> UpdateAssignmentAsync(ProjectResourceAssignment assignment);
Task DeleteAssignmentAsync(string assignmentId);
Task<IEnumerable<ProjectResourceAssignment>> GetAssignmentsAsync(string projectShortName);
```

**Responsibilities:**
- Resource CRUD
- Resource-project assignments
- Assignment lifecycle

## Provider Management

### IProviderService
```csharp
Task<IEnumerable<Provider>> GetProvidersAsync();
Task<Provider> GetProviderAsync(string providerName);
Task<Provider> CreateProviderAsync(ProviderCreate request);
Task<Provider> UpdateProviderAsync(Provider provider);
Task DeleteProviderAsync(string providerName);
```

**Responsibilities:**
- Provider CRUD
- Provider metadata

## Location Management

### ILocationService
```csharp
Task<IEnumerable<Location>> GetLocationsAsync();
Task<Location> GetLocationAsync(string locationName);
Task<Location> CreateLocationAsync(LocationCreate request);
Task<Location> UpdateLocationAsync(Location location);
Task DeleteLocationAsync(string locationName);
```

**Responsibilities:**
- Location CRUD
- Location associations

## Additional Services

### IProjectTagService
- Tag CRUD operations
- Tag assignments to projects

### IProjectRequestService
- Request creation and state management
- Request acceptance/rejection
- Request lifecycle

### IActionLinkService
- Action link management
- Link resolution

### IAdministrationService
- System-level operations
- Configuration management
- Statistics

### IOtherService
- Miscellaneous operations
- Utility functions

## HEAppE Integration Services

### IHeappeIntegrationRetrieverService
```csharp
Task<ProjectResourceInfo> GetProjectResourceAllocationAsync(string projectShortName, string resourceName);
Task<List<Location>> GetValidLocationsByIdAsync(params string[] locationIds);
Task<string> GetHeappeInstanceVersionAsync(string heappeUrl);
```

**Responsibilities:**
- HEAppE cluster information
- Resource allocation queries
- Version detection

### IHeappeSynchronizationService
```csharp
Task SynchronizeProjectResourcesAsync(string projectShortName);
```

**Responsibilities:**
- Trigger project synchronization
- Manage sync state

## Service Registration

All services are registered in `CoreModule.cs`:

```csharp
public static IServiceCollection AddCoreModule(this IServiceCollection services)
{
    services.AddScoped<IUserService, UserService>();
    services.AddScoped<IProjectService, ProjectService>();
    services.AddScoped<IProjectResourceService, ProjectResourceService>();
    services.AddScoped<IProviderService, ProviderService>();
    // ... and many more
    return services;
}
```

## Error Handling

All services implement consistent error handling:
- `RecordNotFoundException` — Resource not found
- `RecordAlreadyExistException` — Duplicate resource
- `ValidationException` — Invalid input data
- `UnauthorizedAccessException` — Permission denied
- `DatabaseCommitException` — Database operation failed

## Testing

Each service has corresponding unit tests in:
- `tests/Modules/Core/Application.Tests/`

## Performance Considerations

- Lazy loading for relationships
- Query optimization
- Caching where appropriate (see [Caching](features/caching.md))
- Async/await throughout

## Status

✅ Fully implemented and production-ready

- 14+ application services
- Comprehensive CRUD operations
- Integration with HEAppE and Waldur
- Full error handling
