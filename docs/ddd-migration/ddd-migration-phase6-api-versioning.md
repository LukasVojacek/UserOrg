# DDD Migration Phase 6: API Endpoint Refactoring with Gateway Versioning

**Phase Number:** 6 of 10  
**Status:** 📋 Ready to Implement  
**Duration:** 1-2 days  
**Effort:** Medium-High  
**Dependencies:** Phases 1-5 complete  

---

## Overview

In Phase 6, you will refactor all API endpoints to support both:
1. **v1 (UUID-based)** - Backward compatible, deprecated
2. **v2 (Natural ID-based)** - New DDD approach, recommended

Using **path-based versioning on separate routes**, the Gateway (YARP) routes traffic:
- `GET /projects/...` → `/api/projects/...` (v1 - UUID)
- `GET /v2/Projects/...` → `/api/v2/Projects/...` (v2 - Natural ID)

---

## 📋 Key Concepts

### Path-Based Versioning (via Gateway)

```
Gateway Routing:
  GET /projects/{id}      → UserOrg /api/projects/{id}        (v1 - UUID)
  GET /v2/projects/{id}   → UserOrg /api/v2/Projects/{id}     (v2 - Natural ID)

UserOrg API Endpoints:
  /api/projects/{projectId}        ← V1 (UUID)
  /api/v2/Projects/{projectShortName}  ← V2 (Natural ID)
```

### Version-Specific DTOs

```csharp
// V1 DTO - includes Id for backward compatibility
record ProjectV1Dto(
    string Id,           // ← INCLUDED
    string Name,
    string ShortName
);

// V2 DTO - no Id field (DDD principle)
record ProjectV2Dto(
    string Name,
    string ShortName     // ← Natural ID instead
);
```

### Architecture Pattern

```
External Client
    ↓
[Gateway - YARP]
    ├─ GET /projects/{id} → /api/projects/{id}
    └─ GET /v2/projects/{id} → /api/v2/Projects/{id}
    ↓
[UserOrg API]
    ├─ V1 Controller: /api/projects/{projectId}
    └─ V2 Controller: /api/v2/Projects/{projectShortName}
    ↓
Response (V1DTO or V2DTO)
```

---

## 🔧 Implementation Steps

### Step 1: Add Versioning Middleware

Create `Middleware/ApiVersionMiddleware.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace UserOrg.Core.API.Middleware;

/// <summary>
/// Extracts API version from query parameter and sets context
/// </summary>
public class ApiVersionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiVersionMiddleware> _logger;

    public ApiVersionMiddleware(RequestDelegate next, ILogger<ApiVersionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var versionParam = context.Request.Query["version"].ToString();
        
        // Determine version (default to v1 if not specified)
        var version = versionParam switch
        {
            "2" => ApiVersion.V2,
            "1" or "" or null => ApiVersion.V1,
            _ => ApiVersion.V1  // Unknown versions default to v1
        };

        _logger.LogInformation("API Request version: {Version}, Endpoint: {Path}", 
            version, context.Request.Path);
        
        // Store in context for handler access
        context.Items["ApiVersion"] = version;
        
        // Set response header indicating version used
        context.Response.Headers["X-API-Version"] = version.ToString();

        await _next(context);
    }
}

/// <summary>
/// API version enumeration
/// </summary>
public enum ApiVersion
{
    V1 = 1,
    V2 = 2
}
```

### Step 2: Create Version Provider Service

Create `Services/IApiVersionProvider.cs`:

```csharp
namespace UserOrg.Core.API.Services;

public interface IApiVersionProvider
{
    ApiVersion GetCurrentVersion(HttpContext context);
    bool IsVersion(HttpContext context, ApiVersion version);
    ApiVersion GetDefaultVersion();
}

public class ApiVersionProvider : IApiVersionProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const ApiVersion DefaultVersion = ApiVersion.V1;

    public ApiVersionProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ApiVersion GetCurrentVersion(HttpContext context)
    {
        if (context.Items.TryGetValue("ApiVersion", out var versionObj) && 
            versionObj is ApiVersion version)
        {
            return version;
        }
        return DefaultVersion;
    }

    public bool IsVersion(HttpContext context, ApiVersion version)
    {
        return GetCurrentVersion(context) == version;
    }

    public ApiVersion GetDefaultVersion() => DefaultVersion;
}
```

### Step 3: Create Version Router Service

Create `Services/IApiVersionRouter.cs`:

```csharp
namespace UserOrg.Core.API.Services;

public interface IVersionedHandler<in TRequest, out TResponse>
    where TRequest : class
    where TResponse : class
{
    Task<Result<TResponse>> Handle(TRequest request);
}

public interface IApiVersionRouter
{
    IVersionedHandler<TRequest, TResponse> GetHandler<TRequest, TResponse>(
        ApiVersion version) 
        where TRequest : class 
        where TResponse : class;
}

public class ApiVersionRouter : IApiVersionRouter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApiVersionRouter> _logger;

    public ApiVersionRouter(IServiceProvider serviceProvider, 
        ILogger<ApiVersionRouter> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IVersionedHandler<TRequest, TResponse> GetHandler<TRequest, TResponse>(
        ApiVersion version) 
        where TRequest : class 
        where TResponse : class
    {
        var service = _serviceProvider.GetService(
            typeof(IVersionedHandler<TRequest, TResponse>));
        
        if (service == null)
        {
            _logger.LogError("Handler not found for version {Version}", version);
            throw new InvalidOperationException(
                $"Handler not found for version {version}");
        }

        return (IVersionedHandler<TRequest, TResponse>)service;
    }
}
```

### Step 4: Create Version-Specific DTOs

Create `Dto/V1/ProjectV1Dto.cs`:

```csharp
namespace UserOrg.Core.API.Dto.V1;

/// <summary>
/// V1 Project DTO - UUID-based (backward compatible)
/// Includes Id field for clients expecting it
/// </summary>
public record ProjectV1Dto(
    string Id,                                  // ← V1 includes Id
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags)
{
    public static ProjectV1Dto Empty => new(
        string.Empty, string.Empty, string.Empty, 
        DateTime.MinValue, null, Array.Empty<string>());
}
```

Create `Dto/V2/ProjectV2Dto.cs`:

```csharp
namespace UserOrg.Core.API.Dto.V2;

/// <summary>
/// V2 Project DTO - Natural ID-based (DDD)
/// No Id field - uses natural identifier (ShortName) instead
/// </summary>
public record ProjectV2Dto(
    string Name,
    string ShortName,                           // ← Natural identifier
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags)
{
    public static ProjectV2Dto Empty => new(
        string.Empty, string.Empty,
        DateTime.MinValue, null, Array.Empty<string>());
}
```

### Step 5: Create Version-Specific Handlers

Create `Handlers/Projects/V1GetProjectByShortNameHandler.cs`:

```csharp
namespace UserOrg.Core.API.Handlers.Projects;

/// <summary>
/// V1 Handler - retrieves project by UUID (backward compatible)
/// </summary>
public class V1GetProjectByIdHandler : 
    IVersionedHandler<GetProjectRequest, ProjectV1Dto>
{
    private readonly IReadUserOrgDbContext _readContext;
    private readonly IMapper _mapper;
    private readonly ILogger<V1GetProjectByIdHandler> _logger;

    public V1GetProjectByIdHandler(
        IReadUserOrgDbContext readContext,
        IMapper mapper,
        ILogger<V1GetProjectByIdHandler> logger)
    {
        _readContext = readContext;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<Result<ProjectV1Dto>> Handle(GetProjectRequest request)
    {
        try
        {
            // V1: Parse UUID from request
            if (!ProjectId.TryParse(request.ProjectIdentifier, out var projectId))
            {
                _logger.LogWarning("Invalid project ID: {ProjectId}", 
                    request.ProjectIdentifier);
                return Result.Failure<ProjectV1Dto>("Invalid project identifier");
            }

            // Query by UUID using shadow property
            var project = await _readContext.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => 
                    EF.Property<ProjectId>(p, "Id") == projectId);

            if (project == null)
            {
                return Result.NotFound<ProjectV1Dto>("Project not found");
            }

            var dto = _mapper.Map<ProjectV1Dto>(project);
            return Result.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving project {ProjectId}", 
                request.ProjectIdentifier);
            return Result.Failure<ProjectV1Dto>("Internal server error");
        }
    }
}
```

Create `Handlers/Projects/V2GetProjectByShortNameHandler.cs`:

```csharp
namespace UserOrg.Core.API.Handlers.Projects;

/// <summary>
/// V2 Handler - retrieves project by natural ID (ProjectShortName)
/// </summary>
public class V2GetProjectByShortNameHandler : 
    IVersionedHandler<GetProjectRequest, ProjectV2Dto>
{
    private readonly IReadUserOrgDbContext _readContext;
    private readonly IMapper _mapper;
    private readonly ILogger<V2GetProjectByShortNameHandler> _logger;

    public V2GetProjectByShortNameHandler(
        IReadUserOrgDbContext readContext,
        IMapper mapper,
        ILogger<V2GetProjectByShortNameHandler> logger)
    {
        _readContext = readContext;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<Result<ProjectV2Dto>> Handle(GetProjectRequest request)
    {
        try
        {
            // V2: Parse natural ID (ProjectShortName)
            var shortNameResult = ProjectShortName.Create(request.ProjectIdentifier);
            if (!shortNameResult.IsSuccess)
            {
                _logger.LogWarning("Invalid project short name: {ShortName}", 
                    request.ProjectIdentifier);
                return Result.Failure<ProjectV2Dto>("Invalid project identifier");
            }

            // Query by natural ID (alternate key)
            var project = await _readContext.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ShortName == shortNameResult.Value);

            if (project == null)
            {
                return Result.NotFound<ProjectV2Dto>("Project not found");
            }

            var dto = _mapper.Map<ProjectV2Dto>(project);
            return Result.Success(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving project {ShortName}", 
                request.ProjectIdentifier);
            return Result.Failure<ProjectV2Dto>("Internal server error");
        }
    }
}
```

### Step 6: Update Controllers

Create/Update `Controllers/ProjectsController.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using UserOrg.Core.API.Dto.V1;
using UserOrg.Core.API.Dto.V2;

namespace UserOrg.Core.API.Controllers;

/// <summary>
/// Projects API - supports v1 (UUID) and v2 (Natural ID) via query parameter
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly IApiVersionProvider _versionProvider;
    private readonly IApiVersionRouter _versionRouter;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IApiVersionProvider versionProvider,
        IApiVersionRouter versionRouter,
        ILogger<ProjectsController> logger)
    {
        _versionProvider = versionProvider;
        _versionRouter = versionRouter;
        _logger = logger;
    }

    /// <summary>
    /// Get project by identifier
    /// 
    /// Query Parameters:
    /// - version=1 (default): UUID-based (v1 - deprecated)
    /// - version=2: Natural ID based (v2 - recommended)
    /// 
    /// Examples:
    /// GET /api/projects/550e8400-e29b-41d4?version=1     (v1)
    /// GET /api/projects/LEX?version=2                     (v2)
    /// GET /api/projects/550e8400-e29b-41d4                (defaults to v1)
    /// </summary>
    [HttpGet("{projectIdentifier}")]
    [ProducesResponseType(typeof(ProjectV1Dto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProjectV2Dto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProject(string projectIdentifier)
    {
        var version = _versionProvider.GetCurrentVersion(HttpContext);
        
        _logger.LogInformation(
            "GetProject request - Identifier: {Identifier}, Version: {Version}", 
            projectIdentifier, version);

        var request = new GetProjectRequest(projectIdentifier);

        return version switch
        {
            ApiVersion.V1 => await HandleV1GetProject(request),
            ApiVersion.V2 => await HandleV2GetProject(request),
            _ => throw new InvalidOperationException($"Version {version} not supported")
        };
    }

    private async Task<IActionResult> HandleV1GetProject(GetProjectRequest request)
    {
        var result = await _versionRouter
            .GetHandler<GetProjectRequest, ProjectV1Dto>(ApiVersion.V1)
            .Handle(request);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { error = result.Error }),
                _ => BadRequest(new { error = result.Error })
            };
        }

        return Ok(result.Value);
    }

    private async Task<IActionResult> HandleV2GetProject(GetProjectRequest request)
    {
        var result = await _versionRouter
            .GetHandler<GetProjectRequest, ProjectV2Dto>(ApiVersion.V2)
            .Handle(request);

        if (!result.IsSuccess)
        {
            return result.ErrorCode switch
            {
                "NotFound" => NotFound(new { error = result.Error }),
                _ => BadRequest(new { error = result.Error })
            };
        }

        return Ok(result.Value);
    }
}

public record GetProjectRequest(string ProjectIdentifier);
```

### Step 7: Register in DI Container

Update `Program.cs`:

```csharp
using UserOrg.Core.API.Middleware;
using UserOrg.Core.API.Services;
using UserOrg.Core.API.Handlers.Projects;

var builder = WebApplicationBuilder.CreateBuilder(args);

// Add API versioning services
builder.Services.AddScoped<IApiVersionProvider, ApiVersionProvider>();
builder.Services.AddScoped<IApiVersionRouter, ApiVersionRouter>();

// Register V1 Handlers
builder.Services.AddScoped<IVersionedHandler<GetProjectRequest, ProjectV1Dto>,
    V1GetProjectByIdHandler>();
// ... register other V1 handlers

// Register V2 Handlers
builder.Services.AddScoped<IVersionedHandler<GetProjectRequest, ProjectV2Dto>,
    V2GetProjectByShortNameHandler>();
// ... register other V2 handlers

builder.Services
    .AddControllers()
    .AddMappers();

var app = builder.Build();

// Add versioning middleware
app.UseMiddleware<ApiVersionMiddleware>();

// Enable OpenAPI versioning (see api-versioning-query-parameter.md)
app.MapScalarApiReference();

app.MapControllers();
app.Run();
```

### Step 8: Add AutoMapper Profiles

Create `Mappings/ProjectMappingProfile.cs`:

```csharp
using AutoMapper;
using UserOrg.Core.API.Dto.V1;
using UserOrg.Core.API.Dto.V2;
using UserOrg.Core.Domain.Entities;

namespace UserOrg.Core.API.Mappings;

public class ProjectMappingProfile : Profile
{
    public ProjectMappingProfile()
    {
        // V1 Mapping (include Id)
        CreateMap<Project, ProjectV1Dto>()
            .ForMember(dest => dest.Id,
                opt => opt.MapFrom(src => 
                    EF.Property<ProjectId>(src, "Id").ToString()))
            .ForMember(dest => dest.Name, 
                opt => opt.MapFrom(src => src.Name))
            .ForMember(dest => dest.ShortName,
                opt => opt.MapFrom(src => src.ShortName.Value))
            .ForMember(dest => dest.Tags,
                opt => opt.MapFrom(src => 
                    src.ProjectTags.Select(t => t.Name).ToList()));

        // V2 Mapping (no Id, uses natural identifier)
        CreateMap<Project, ProjectV2Dto>()
            .ForMember(dest => dest.ShortName,
                opt => opt.MapFrom(src => src.ShortName.Value))
            .ForMember(dest => dest.Tags,
                opt => opt.MapFrom(src => 
                    src.ProjectTags.Select(t => t.Name).ToList()));
    }
}
```

---

## 🔄 Model Evolution: Beyond Endpoint Versioning

### When NOT to Create New Endpoint Version

Sometimes you need to change the model/contract, but creating a new entire endpoint version (`/api/v3`) is overkill. Use **model evolution** instead:

**Scenario:** Your v2 Project model needs a new optional field "Category"

**Option A: Create /api/v3 (❌ Unnecessary)**
```
/api/projects/LEX?version=2     → ProjectV2Dto (old)
/api/projects/LEX?version=3     → ProjectV3Dto (with category)
```
Problem: Duplicates entire endpoint logic

**Option B: Evolve the model (✅ Recommended)**
```
/api/projects/LEX?version=2     → ProjectV2Dto (v2.1 with category)
Old ProjectV2Dto marked @Obsolete
```
Benefit: Same endpoint, evolved contract

### Model Evolution Pattern

```csharp
// BEFORE (v2.0)
public record ProjectV2Dto(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags);

// AFTER (v2.1) - evolved, same endpoint
public record ProjectV2Dto(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags,
    string? Category = null);  // ← NEW optional field

// Mark old DTO as deprecated
[Obsolete("Use new ProjectV2Dto with Category field")]
public record ProjectV2DtoV1(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags);
```

### When to Evolve vs. Create New Version

| Scenario | Action | Reason |
|----------|--------|--------|
| Add optional field | Evolve (same /v2) | Backward compatible |
| Add required field | New version (/v3) | Breaking change |
| Remove field | New version (/v3) | Breaking change |
| Change field type | New version (/v3) | Breaking change |
| Rename field | New version (/v3) | Breaking change |
| Change validation rules | Evaluate impact | May need new version |

### Benefits of Model Evolution

✅ Reduce endpoint explosion (no /v3, /v4, /v5...)  
✅ Simpler handler logic (one handler per version)  
✅ Easier client adoption (optional changes)  
✅ Maintains backward compatibility  
✅ Clear deprecation path  

### Implementation Example

See full documentation: **[Model Versioning - Evolution](./model-versioning-evolution.md)**

Includes:
- Adding optional fields to DTOs
- Marking old models as @Obsolete
- AutoMapper pattern updates
- Backward compatibility testing
- Client communication templates

---

### Unit Tests for Version Routing

```csharp
[TestClass]
public class ProjectsVersioningTests
{
    private Mock<IReadUserOrgDbContext> _mockReadContext;
    private Mock<IMapper> _mockMapper;
    private Mock<ILogger<V1GetProjectByIdHandler>> _mockLogger;
    private V1GetProjectByIdHandler _handler;

    [TestInitialize]
    public void Setup()
    {
        _mockReadContext = new Mock<IReadUserOrgDbContext>();
        _mockMapper = new Mock<IMapper>();
        _mockLogger = new Mock<ILogger<V1GetProjectByIdHandler>>();
        _handler = new V1GetProjectByIdHandler(_mockReadContext.Object, 
            _mockMapper.Object, _mockLogger.Object);
    }

    [TestMethod]
    public async Task Handle_ValidUUID_ReturnsProjectV1Dto()
    {
        // Arrange
        var projectId = ProjectId.Create();
        var request = new GetProjectRequest(projectId.ToString());
        var project = Project.Create("Test", 
            ProjectShortName.Create("TST").Value, 
            DateTime.Now, null).Value;

        var expectedDto = new ProjectV1Dto(
            projectId.ToString(), "Test", "TST", 
            DateTime.Now, null, new List<string>());

        _mockReadContext.Setup(x => x.Projects)
            .Returns(new[] { project }.AsQueryable().AsAsyncQueryable());
        _mockMapper.Setup(x => x.Map<ProjectV1Dto>(It.IsAny<Project>()))
            .Returns(expectedDto);

        // Act
        var result = await _handler.Handle(request);

        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(expectedDto, result.Value);
    }

    [TestMethod]
    public async Task Handle_InvalidUUID_ReturnsFAILURE()
    {
        // Arrange
        var request = new GetProjectRequest("invalid-uuid");

        // Act
        var result = await _handler.Handle(request);

        // Assert
        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Error.Contains("Invalid"));
    }
}
```

### Integration Tests

```csharp
[TestClass]
public class ProjectsEndpointTests
{
    private TestServer _server;
    private HttpClient _client;

    [TestInitialize]
    public void Setup()
    {
        var builder = new WebHostBuilder()
            .UseStartup<Startup>();
        _server = new TestServer(builder);
        _client = _server.CreateClient();
    }

    [TestMethod]
    public async Task GetProject_V1_ReturnsIdField()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/projects/550e8400-e29b-41d4-a716-446655440000?version=1");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsAsync<ProjectV1Dto>();
        Assert.IsNotNull(json.Id);
    }

    [TestMethod]
    public async Task GetProject_V2_NoIdField()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/projects/LEX?version=2");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsAsync<JObject>();
        Assert.IsNull(json["id"]);  // V2 should not have id
        Assert.IsNotNull(json["shortName"]);
    }

    [TestMethod]
    public async Task GetProject_DefaultVersion_IsV1()
    {
        // Act
        var response = await _client.GetAsync(
            "/api/projects/550e8400-e29b-41d4-a716-446655440000");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("V1", 
            response.Headers.GetValues("X-API-Version").First());
    }
}
```

---

## 📋 Checklist

### Version Infrastructure
- [ ] ApiVersionMiddleware created
- [ ] ApiVersion enum defined
- [ ] IApiVersionProvider implemented
- [ ] IApiVersionRouter implemented
- [ ] IVersionedHandler interface defined

### DTOs
- [ ] V1 DTOs created (with Id)
- [ ] V2 DTOs created (without Id, natural IDs)
- [ ] AutoMapper profiles configured
- [ ] DTOs include proper documentation

### Handlers
- [ ] V1 handlers created (UUID-based)
- [ ] V2 handlers created (Natural ID-based)
- [ ] Handlers follow consistent pattern
- [ ] Error handling implemented
- [ ] Logging added

### Controllers
- [ ] Controllers accept query parameter
- [ ] Controllers route to version handlers
- [ ] Response headers set (X-API-Version)
- [ ] OpenAPI attributes added
- [ ] Documentation strings added

### DI Registration
- [ ] Middleware registered
- [ ] Version services registered
- [ ] V1 handlers registered
- [ ] V2 handlers registered
- [ ] MapperProfile registered

### Testing
- [ ] Unit tests created
- [ ] Integration tests created
- [ ] All tests passing
- [ ] Version routing verified
- [ ] Error cases covered

### Documentation
- [ ] OpenAPI specs created (v1 and v2)
- [ ] Scalar UI configured
- [ ] Client migration guide written
- [ ] Deprecation warnings added
- [ ] Examples documented

---

## 📚 Related Documentation

- **[📖 DDD Migration Documentation Index](./README.md)** - Complete reading guide and navigation
- **[Model Versioning - Evolution](./model-versioning-evolution.md)** - How to evolve models without creating new endpoint versions
- **[API Versioning - Gateway Routing](./api-versioning-gateway-routing.md)** - Primary gateway-based versioning strategy
- **[Entity Relationship Complete Map](./entity-relationship-complete-map.md)** - All 24 entities and aggregates
- **[Phase 1: Architecture Analysis](./ddd-migration-phase1-analysis.md)** - Understanding aggregate roots

---

## 🚀 Next Steps

1. **Complete checklist above**
2. **Review Model Evolution** ([model-versioning-evolution.md](./model-versioning-evolution.md)) for non-breaking changes
3. **Test both V1 and V2 endpoints** with real clients
4. **Proceed to Phase 7:** Application Layer Updates
5. **Deploy to staging** and validate with external teams

---

**Phase Status:** 📋 Ready to Implement  
**Document Version:** 1.0  
**Last Updated:** 2026-04-24
