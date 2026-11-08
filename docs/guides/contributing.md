# Contributing Guide

Code standards, testing, pull request process, and development conventions.

## Getting Started

See [Getting Started Guide](getting-started.md) for local setup.

## Code Style & Conventions

### Naming

Follow PascalCase for public members, camelCase for private/local:

```csharp
// Public
public class ProjectService { }
public async Task<Project> GetProjectAsync(string id) { }

// Private
private readonly IRepository<Project> _projectRepository;
private async Task ValidateAsync() { }
```

### File Organization

Each class in its own file:

```
src/Core/Application/UserOrg/Services/
├── ProjectService.cs
├── UserService.cs
└── IServices/
    ├── IProjectService.cs
    └── IUserService.cs
```

### Namespace Structure

Mirrors directory structure:

```csharp
namespace UserOrganization.Core.Application.UserOrg.Services
{
    public class ProjectService { }
}
```

## Architecture Patterns

### Service Layer

All business logic lives in application services:

```csharp
public class ProjectService : IProjectService
{
    private readonly IProjectRepository _repository;
    private readonly ILogger<ProjectService> _logger;
    
    public async Task<Project> CreateProjectAsync(ProjectCreate request)
    {
        // Validate
        if (string.IsNullOrEmpty(request.ShortName))
            throw new ValidationException("Project short name is required");
        
        // Create
        var project = new Project { ShortName = request.ShortName };
        await _repository.AddAsync(project);
        
        // Log
        _logger.LogInformation("Project created: {ProjectId}", project.Id);
        
        return project;
    }
}
```

### Repository Pattern

Access data through repositories:

```csharp
public interface IProjectRepository : IRepository<Project>
{
    Task<Project> GetByShortNameAsync(string shortName);
}

// Usage in service
var project = await _projectRepository.GetByShortNameAsync("myproject");
```

### Dependency Injection

Inject dependencies through constructor:

```csharp
public class ProjectService
{
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<ProjectService> _logger;
    
    public ProjectService(
        IProjectRepository projectRepository,
        ILogger<ProjectService> logger)
    {
        _projectRepository = projectRepository;
        _logger = logger;
    }
}
```

## Testing

### Unit Tests

Test a single unit in isolation:

```csharp
[Fact]
public async Task CreateProjectAsync_WithValidRequest_ReturnsProject()
{
    // Arrange
    var service = new ProjectService(_repository, _logger);
    var request = new ProjectCreate { ShortName = "testproject" };
    
    // Act
    var result = await service.CreateProjectAsync(request);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("testproject", result.ShortName);
}
```

### Integration Tests

Test with real dependencies:

```csharp
[Fact]
public async Task CreateProjectEndpoint_WithValidRequest_Returns201()
{
    // Arrange
    using var client = new HttpClient();
    var request = new ProjectCreate { ShortName = "testproject" };
    
    // Act
    var response = await client.PostAsJsonAsync("/api/Project", request);
    
    // Assert
    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
}
```

### Test Naming

Use the pattern: `MethodName_Condition_ExpectedResult`

```csharp
CreateProjectAsync_WithNullShortName_ThrowsValidationException
CreateProjectAsync_WithValidRequest_ReturnsProject
GetProjectAsync_WithNonExistentId_ThrowsNotFoundException
```

### Running Tests

```bash
# All tests (what CI runs)
dotnet test --solution UserOrganization.slnx

# With coverage
dotnet test /p:CollectCoverage=true
```

This repo runs xUnit v3 on the Microsoft Testing Platform, which changes two things:

**`--filter` does not work.** `dotnet test --filter "ClassName"` runs **zero tests and
exits 0** — it looks like a pass. To run a subset, build the project and run its executable
from `artifacts/` (this repo sets `UseArtifactsOutput`, so output is not next to the project):

```bash
dotnet build Tests/Modules/Integrations/Heappe.UnitTests/Heappe.UnitTests.csproj --no-incremental
./artifacts/bin/Heappe.UnitTests/debug/Heappe.UnitTests.exe --filter-class "*.MyTests"
```

**Check the build succeeded before trusting the result.** Running the executable directly
means a *failed* build leaves the previous binary in place and it will happily report green —
the old code passing its old tests. Always confirm the build's exit code, and confirm the run
reports a non-zero test count. Pass an explicit `.csproj` path rather than a folder: a folder
containing two project files fails with `MSB1050`, which is exactly how this bites.

## Error Handling

### Use Custom Exceptions

```csharp
// Define custom exception
public class ProjectNotFoundException : Exception
{
    public ProjectNotFoundException(string shortName)
        : base($"Project '{shortName}' not found") { }
}

// Use it
if (project == null)
    throw new ProjectNotFoundException(shortName);
```

### Return HTTP Status Correctly

```csharp
app.MapPost("/api/Project/{id}", CreateProjectHandler)
    .Produces<Project>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status500InternalServerError);

private async Task<IResult> CreateProjectHandler(ProjectCreate request)
{
    try
    {
        var project = await _projectService.CreateProjectAsync(request);
        return Results.Created($"/api/Project/{project.Id}", project);
    }
    catch (ValidationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}
```

## Logging

Use structured logging with LogLevel:

```csharp
_logger.LogInformation("Project created: {@ProjectData}", new { project.Id, project.Name });
_logger.LogWarning("Project update missing field: {FieldName}", fieldName);
_logger.LogError(ex, "Failed to delete project: {ProjectId}", projectId);
```

## API Endpoints

### Endpoint Naming

```csharp
app.MapPost("/api/Project", CreateProjectHandler)
    .WithName("CreateProject")
    .WithOpenApi();

app.MapGet("/api/Project/{projectId}", GetProjectHandler)
    .WithName("GetProject")
    .WithOpenApi();
```

### Documentation

Add OpenAPI documentation:

```csharp
app.MapPost("/api/Project", CreateProjectHandler)
    .Produces<Project>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest)
    .WithOpenApi(operation =>
    {
        operation.Summary = "Create a new project";
        operation.Description = "Creates a new research project with the provided details";
        return operation;
    });
```

## Database Migrations

### Creating Migrations

```bash
cd src/Core/Infrastructure.DataAccess
dotnet ef migrations add AddNewTable -c UserOrgDbContext -o Persistence/Migrations
```

Migration files go in `Persistence/Migrations/`.

### Naming Migrations

Use descriptive names that explain what changed:
- ✅ `AddProjectTagsTable`
- ✅ `AddUserPreferences`
- ❌ `Update1`
- ❌ `Migration20240101`

### Reviewing Migrations

Always review generated migrations before committing:

```bash
# View the migration file
cat src/Core/Infrastructure.DataAccess/Persistence/Migrations/[MigrationName].cs
```

## Pull Request Process

### Before Creating a PR

1. **Update branch:** `git pull origin develop`
2. **Create feature branch:** `git checkout -b feature/my-feature`
3. **Run tests:** `dotnet test`
4. **Check style:** Review your code for conventions
5. **Update docs:** Add/update relevant documentation

### PR Title Format

Follow conventional commits:

```
feat: add project tagging feature
fix: handle null reference in project service
docs: update deployment guide
refactor: simplify permission validation
test: add integration tests for project endpoint
```

### PR Description Template

```markdown
## Description
Brief description of changes

## Related Issue
Fixes #123

## Changes
- Change 1
- Change 2

## Testing
- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] Manual testing done

## Documentation
- [ ] Updated relevant docs
- [ ] Updated API docs
```

### Code Review Checklist

Before submitting, ensure:
- [ ] Code follows style guide
- [ ] Tests are included and passing
- [ ] Documentation is updated
- [ ] No breaking changes (or documented)
- [ ] Error handling is complete
- [ ] Logging is appropriate
- [ ] Database migrations reviewed (if applicable)

## Common Guidelines

### Comments

Only add comments for non-obvious logic:

```csharp
// ✅ Good: explains why
// Resource must be loaded before assignment to validate access
var resource = await _resourceService.GetAsync(request.ResourceId);

// ❌ Bad: states the obvious
// Get the resource
var resource = await _resourceService.GetAsync(request.ResourceId);
```

### Async/Await

Use async throughout:

```csharp
// ✅ Good
public async Task<Project> GetProjectAsync(string id)
{
    return await _repository.GetByIdAsync(id);
}

// ❌ Bad
public async Task<Project> GetProjectAsync(string id)
{
    return _repository.GetById(id); // Blocking call
}
```

### Null Checks

Use explicit null checks:

```csharp
// ✅ Good
if (project == null)
    throw new ProjectNotFoundException(id);

// ⚠️ Avoid null-coalescing for validation
var project = await _repository.GetByIdAsync(id) 
    ?? throw new ProjectNotFoundException(id);
```

## Branch Strategy

### Branch Naming

```
feature/add-project-tags
fix/null-reference-error
docs/update-deployment-guide
refactor/simplify-permission-logic
```

### Main Branches

- `main` — Production releases
- `develop` — Development/staging
- Feature branches from `develop`

## Continuous Integration

All PRs automatically:
- Run unit tests
- Run integration tests
- Check code style
- Generate coverage reports

## Security Considerations

- **No secrets in code** — Use Vault or environment variables
- **Input validation** — Always validate user input
- **SQL injection prevention** — Use parameterized queries (Entity Framework handles this)
- **Authentication checks** — Verify tokens before processing requests
- **HTTPS in production** — Enforce secure communication

## Performance Best Practices

- Use `AsNoTracking()` for read-only queries
- Batch operations when possible
- Index frequently queried columns
- Cache appropriately (see [Caching Guide](../components/core/features/caching.md))

## Questions or Issues?

1. Check existing issues and PRs
2. Discuss in team channel (Slack, Teams, etc.)
3. Reference relevant documentation
4. Ask in PR comments

## Status

✅ Complete contributing guide

- Code style and conventions
- Testing standards
- PR process
- Common guidelines
- Security and performance
