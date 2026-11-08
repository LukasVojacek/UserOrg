# Multitenancy Code Patterns — Quick Reference

Copy-paste templates and patterns for consistent implementation.

---

## Pattern 1: Ownership Check Helper

Use this in **every service** that modifies entity data:

```csharp
namespace UserOrganizationManagement.Application.UserOrg.Services;

public partial class ProjectService
{
    /// <summary>
    /// Verifies that caller owns the project or is PlatformAdmin.
    /// Throws ForbiddenException if caller is a ProviderAdmin of a different provider.
    /// </summary>
    private static void EnsureCallerOwnsProject(Project project, Guid? callerProviderId)
    {
        // PlatformAdmin (null providerId) always allowed
        if (callerProviderId is null)
            return;

        // ProviderAdmin must own the project
        if (project.ProviderId != callerProviderId)
            throw new ForbiddenException(
                $"Access denied. Project belongs to provider {project.ProviderId}.");
    }
}
```

**For other entities**, just copy and rename:
```csharp
private static void EnsureCallerOwnsLocation(Location location, Guid? callerProviderId)
private static void EnsureCallerOwnsProvider(Provider provider, Guid? callerProviderId)
private static void EnsureCallerOwnsProjectResource(ProjectResource resource, Guid? callerProviderId)
```

---

## Pattern 2: Write Method with Ownership Guard

Apply this pattern to **every method that mutates data**:

```csharp
public async Task UpdateProjectAsync(
    ProjectId projectId,
    UpdateProjectRequest request,
    Guid? callerProviderId,  // ← New parameter
    CancellationToken ct = default)
{
    var project = await _projectRepository.GetByIdAsync(projectId, ct);
    
    if (project is null)
        throw new ProjectNotFoundException(projectId);

    // ← GUARD: Check ownership BEFORE any mutation
    EnsureCallerOwnsProject(project, callerProviderId);

    // ← NOW safe to modify
    project.Name = request.Name;
    project.Description = request.Description;

    await _context.SaveChangesAsync(ct);
}
```

**Key points:**
1. ✅ Add `Guid? callerProviderId` parameter
2. ✅ Guard placement: right after fetch, before any modification
3. ✅ Never modify first, then check

---

## Pattern 3: Create Method with Data Stamping

When creating new entities, stamp the caller's provider:

```csharp
public async Task<Project> InsertProjectAsync(
    CreateProjectRequest request,
    Guid? callerProviderId,  // ← New parameter
    CancellationToken ct = default)
{
    var project = new Project
    {
        Id = ProjectId.New(),
        Name = request.Name,
        ShortName = request.ShortName,
        Description = request.Description,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        CreatedAt = DateTime.UtcNow,
    };

    // ← STAMP: Associate with creator's provider (if not PlatformAdmin)
    if (callerProviderId.HasValue)
        project.ProviderId = callerProviderId;

    _projectRepository.Add(project);
    await _context.SaveChangesAsync(ct);

    return project;
}
```

---

## Pattern 4: Endpoint Propagating CallerProviderId

Apply this pattern to **every endpoint that calls a service**:

```csharp
namespace UserOrganizationManagement.API.Endpoints.Project;

public static class ProjectEndpoint
{
    [HttpPut("{shortName}")]
    public static async Task<IResult> UpdateProject(
        [FromRoute] ProjectShortName shortName,
        [FromBody] UpdateProjectRequest request,
        HttpContext httpContext,
        IProjectService projectService,
        CancellationToken ct)
    {
        // ← Extract caller's provider context
        httpContext.TryGetProviderId(out var callerProviderId);

        // ← Pass to service
        await projectService.UpdateProjectAsync(
            shortName,
            request,
            callerProviderId,  // ← Include here
            ct);

        return Results.NoContent();
    }

    [HttpPost]
    public static async Task<IResult> CreateProject(
        [FromBody] CreateProjectRequest request,
        HttpContext httpContext,
        IProjectService projectService,
        CancellationToken ct)
    {
        httpContext.TryGetProviderId(out var callerProviderId);

        var project = await projectService.InsertProjectAsync(
            request,
            callerProviderId,  // ← Include here
            ct);

        return Results.Created($"/Project/{project.ShortName}", project);
    }
}
```

---

## Pattern 5: ReadOnly Toggle Implementation

Add this to services that support ReadOnly mode:

```csharp
public async Task SetProjectModeAsync(
    ProjectShortName shortName,
    EOperationMode mode,
    Guid? callerProviderId,
    CancellationToken ct = default)
{
    var project = await _projectRepository.GetByShortNameAsync(shortName, ct);
    
    if (project is null)
        throw new ProjectNotFoundException();

    // ← Ownership check (only owner can toggle their own mode)
    EnsureCallerOwnsProject(project, callerProviderId);

    // ← Guard: Cannot unlock system-managed records
    if (project.Mode == EOperationMode.ReadOnly && project.ServiceName != "Lexis")
        throw new RecordReadOnlyException(
            "Cannot modify mode of system-managed records. " +
            "Contact platform administrator if needed.");

    // ← Safe to modify
    project.Mode = mode;
    // Note: ServiceName stays unchanged — it tracks who controls the lock

    await _context.SaveChangesAsync(ct);
}
```

---

## Pattern 6: Audit Logging for Admin Override

If PlatformAdmin needs to override ownership, log it:

```csharp
public async Task UpdateProjectAsync(
    ProjectId projectId,
    UpdateProjectRequest request,
    Guid? callerProviderId,
    bool adminOverride = false,
    string? overrideReason = null,
    CancellationToken ct = default)
{
    var project = await _projectRepository.GetByIdAsync(projectId, ct);
    
    if (project is null)
        throw new ProjectNotFoundException(projectId);

    // Standard ownership check
    if (callerProviderId is not null && project.ProviderId != callerProviderId)
    {
        // PlatformAdmin (null) can override, but must log it
        if (callerProviderId is null && !adminOverride)
            throw new ForbiddenException("Admin override required to modify foreign data.");

        if (adminOverride)
        {
            _logger.LogWarning(
                "ADMIN_OVERRIDE: UpdateProject {ProjectId} " +
                "(ProviderId={ProviderId}, Reason={Reason})",
                projectId, project.ProviderId, overrideReason ?? "not provided");
        }
    }

    // Safe to modify
    project.Name = request.Name;
    await _context.SaveChangesAsync(ct);
}
```

**Endpoint version:**
```csharp
[HttpPut("{shortName}")]
public static async Task<IResult> UpdateProject(
    [FromRoute] ProjectShortName shortName,
    [FromBody] UpdateProjectRequest request,
    [FromQuery] bool adminOverride = false,
    [FromQuery] string? reason = null,
    HttpContext httpContext,
    IProjectService projectService,
    CancellationToken ct)
{
    httpContext.TryGetProviderId(out var callerProviderId);
    
    // For PlatformAdmin overrides
    if (callerProviderId is null && adminOverride && string.IsNullOrEmpty(reason))
        return Results.BadRequest("Admin override requires a reason.");

    await projectService.UpdateProjectAsync(
        shortName,
        request,
        callerProviderId,
        adminOverride: adminOverride && callerProviderId is null,
        overrideReason: reason,
        ct);

    return Results.NoContent();
}
```

---

## Pattern 7: Response DTO with LockedBy

Extend your response models:

```csharp
namespace UserOrganizationManagement.API.Models;

public record ProjectModel
{
    public ProjectId Id { get; init; }
    public string Name { get; init; }
    public ProjectShortName ShortName { get; init; }
    public string Description { get; init; }
    
    // ... other fields ...

    /// <summary>
    /// Operation mode: "Unspecified", "ReadOnly", "Editable"
    /// </summary>
    public string Mode { get; init; }

    /// <summary>
    /// Who locked this record (if Mode == ReadOnly).
    /// "Lexis" = tenant admin, "Waldur" = external sync, null = not locked.
    /// </summary>
    public string? LockedBy { get; init; }

    // Computed property for mapping from entity
    public static ProjectModel FromEntity(Project entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        ShortName = entity.ShortName,
        Description = entity.Description,
        Mode = entity.Mode.ToString(),
        LockedBy = entity.Mode == EOperationMode.ReadOnly ? entity.ServiceName : null,
        // ... map other fields ...
    };
}
```

---

## Pattern 8: Unit Test for Ownership Check

Template for unit testing:

```csharp
namespace UserOrganizationManagement.Tests.Application;

[TestFixture]
public class ProjectServiceOwnershipTests
{
    private ProjectService _service;
    private Mock<IProjectRepository> _repositoryMock;
    private Mock<IUserOrgDbContext> _contextMock;

    [SetUp]
    public void Setup()
    {
        _repositoryMock = new Mock<IProjectRepository>();
        _contextMock = new Mock<IUserOrgDbContext>();
        _service = new ProjectService(_repositoryMock.Object, _contextMock.Object);
    }

    [Test]
    public async Task UpdateProject_CallerOwnsSameProvider_Succeeds()
    {
        // Arrange
        var providerId = Guid.NewGuid();
        var projectId = ProjectId.New();
        var project = new Project 
        { 
            Id = projectId, 
            ProviderId = providerId,
            Name = "Original"
        };
        var request = new UpdateProjectRequest { Name = "Updated" };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Act
        await _service.UpdateProjectAsync(
            projectId, 
            request, 
            providerId,  // ← Caller owns same provider
            CancellationToken.None);

        // Assert
        Assert.AreEqual("Updated", project.Name);
        _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()));
    }

    [Test]
    public void UpdateProject_CallerOwnsDifferentProvider_Throws()
    {
        // Arrange
        var ownerProviderId = Guid.NewGuid();
        var callerProviderId = Guid.NewGuid();
        var projectId = ProjectId.New();
        var project = new Project 
        { 
            Id = projectId, 
            ProviderId = ownerProviderId,
            Name = "Original"
        };
        var request = new UpdateProjectRequest { Name = "Hacked" };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Act & Assert
        var ex = Assert.ThrowsAsync<ForbiddenException>(
            () => _service.UpdateProjectAsync(
                projectId, 
                request, 
                callerProviderId,  // ← Different provider!
                CancellationToken.None));

        Assert.IsTrue(ex.Message.Contains("Access denied"));
    }

    [Test]
    public async Task UpdateProject_PlatformAdminWithNullProvider_Succeeds()
    {
        // Arrange
        var ownerProviderId = Guid.NewGuid();
        var projectId = ProjectId.New();
        var project = new Project 
        { 
            Id = projectId, 
            ProviderId = ownerProviderId
        };
        var request = new UpdateProjectRequest { Name = "AdminOverride" };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(projectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Act
        await _service.UpdateProjectAsync(
            projectId, 
            request, 
            null,  // ← PlatformAdmin (no provider)
            CancellationToken.None);

        // Assert
        Assert.AreEqual("AdminOverride", project.Name);
        _contextMock.Verify(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task SetProjectMode_LocksOwnProject_Succeeds()
    {
        // Arrange
        var providerId = Guid.NewGuid();
        var shortName = new ProjectShortName("PROJ");
        var project = new Project 
        { 
            ShortName = shortName, 
            ProviderId = providerId,
            Mode = EOperationMode.Unspecified,
            ServiceName = "Lexis"
        };

        _repositoryMock
            .Setup(r => r.GetByShortNameAsync(shortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Act
        await _service.SetProjectModeAsync(shortName, EOperationMode.ReadOnly, providerId);

        // Assert
        Assert.AreEqual(EOperationMode.ReadOnly, project.Mode);
        Assert.AreEqual("Lexis", project.ServiceName); // Unchanged
    }

    [Test]
    public void SetProjectMode_UnlocksSystemData_Throws()
    {
        // Arrange
        var providerId = Guid.NewGuid();
        var shortName = new ProjectShortName("PROJ");
        var project = new Project 
        { 
            ShortName = shortName, 
            ProviderId = providerId,
            Mode = EOperationMode.ReadOnly,
            ServiceName = "Waldur"  // ← System lock!
        };

        _repositoryMock
            .Setup(r => r.GetByShortNameAsync(shortName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        // Act & Assert
        Assert.ThrowsAsync<RecordReadOnlyException>(
            () => _service.SetProjectModeAsync(shortName, EOperationMode.Editable, providerId));
    }
}
```

---

## Pattern 9: Integration Test for Cross-Tenant Isolation

```csharp
namespace UserOrganizationManagement.Tests.Integration;

[TestFixture]
public class MultitenancyIsolationTests
{
    private TestWebApplicationFactory _factory;
    private HttpClient _clientProviderA;
    private HttpClient _clientProviderB;
    private HttpClient _clientPlatformAdmin;

    [SetUp]
    public void Setup()
    {
        _factory = new TestWebApplicationFactory();
        
        // ProviderAdmin A
        _clientProviderA = _factory.CreateClient(bearerToken: GetTokenForProviderAdmin(Guid.NewGuid()));
        
        // ProviderAdmin B
        _clientProviderB = _factory.CreateClient(bearerToken: GetTokenForProviderAdmin(Guid.NewGuid()));
        
        // PlatformAdmin
        _clientPlatformAdmin = _factory.CreateClient(bearerToken: GetTokenForPlatformAdmin());
    }

    [Test]
    public async Task ProviderAdminCannotUpdateForeignProviderProject()
    {
        // Arrange: Create project in Provider A
        var projectRequest = new CreateProjectRequest { Name = "Project A", ... };
        var createResponse = await _clientProviderA.PostAsJsonAsync("/api/Project", projectRequest);
        var projectId = (await createResponse.Content.ReadAsAsync<ProjectModel>()).Id;

        // Act: Try to update from Provider B
        var updateRequest = new UpdateProjectRequest { Name = "Hacked" };
        var updateResponse = await _clientProviderB.PutAsJsonAsync($"/api/Project/{projectId}", updateRequest);

        // Assert
        Assert.AreEqual(HttpStatusCode.Forbidden, updateResponse.StatusCode);
        // Verify original data intact
        var getResponse = await _clientProviderA.GetAsync($"/api/Project/{projectId}");
        var project = await getResponse.Content.ReadAsAsync<ProjectModel>();
        Assert.AreEqual("Project A", project.Name);
    }

    [Test]
    public async Task ProviderAdminCanUpdateOwnProject()
    {
        // Arrange: Create project in Provider A
        var projectRequest = new CreateProjectRequest { Name = "Project A", ... };
        var createResponse = await _clientProviderA.PostAsJsonAsync("/api/Project", projectRequest);
        var projectId = (await createResponse.Content.ReadAsAsync<ProjectModel>()).Id;

        // Act: Update from same Provider A
        var updateRequest = new UpdateProjectRequest { Name = "Updated A" };
        var updateResponse = await _clientProviderA.PutAsJsonAsync($"/api/Project/{projectId}", updateRequest);

        // Assert
        Assert.AreEqual(HttpStatusCode.NoContent, updateResponse.StatusCode);
        var getResponse = await _clientProviderA.GetAsync($"/api/Project/{projectId}");
        var project = await getResponse.Content.ReadAsAsync<ProjectModel>();
        Assert.AreEqual("Updated A", project.Name);
    }

    [Test]
    public async Task PlatformAdminCanUpdateAnyProject()
    {
        // Arrange: Create project in Provider A
        var projectRequest = new CreateProjectRequest { Name = "Project A", ... };
        var createResponse = await _clientProviderA.PostAsJsonAsync("/api/Project", projectRequest);
        var projectId = (await createResponse.Content.ReadAsAsync<ProjectModel>()).Id;

        // Act: Update as PlatformAdmin
        var updateRequest = new UpdateProjectRequest { Name = "Admin Updated" };
        var updateResponse = await _clientPlatformAdmin.PutAsJsonAsync($"/api/Project/{projectId}", updateRequest);

        // Assert
        Assert.AreEqual(HttpStatusCode.NoContent, updateResponse.StatusCode);
    }
}
```

---

## Summary: The 3-Step Formula

For **every new write operation**:

1. **Accept** `Guid? callerProviderId` parameter
2. **Guard** with `EnsureCallerOwns*` check (before any modification)
3. **Pass** callerProviderId from endpoint via `httpContext.TryGetProviderId()`

That's it. Consistent application of this formula prevents data leaks.

