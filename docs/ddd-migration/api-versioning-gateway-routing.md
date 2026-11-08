# API Versioning via Gateway - Path-Based Strategy

**Document Version:** 2.0  
**Created:** 2026-04-24  
**Updated:** 2026-04-24  
**Purpose:** Gateway-based API versioning with path-based routing on UserOrg API

---

## 📋 Overview

This document describes the **Gateway-based API versioning** strategy where:
- **Gateway** (YARP reverse proxy) routes incoming requests to appropriate API version
- **UserOrg API** exposes both paths: `/api/...` (v1) and `/api/v2/...` (v2)
- **Clean separation** of versions without query parameters
- **Transparent routing** - clients don't need special configuration

### Versioning Model

```
Gateway (Router)
    ↓
[YARP Reverse Proxy]
    ├─ GET /projects/...         → GET /api/projects/...        (V1 - UUID)
    ├─ GET /v2/projects/...      → GET /api/v2/Projects/...     (V2 - Natural ID)
    └─ GET /users/...            → GET /api/users/...           (V1 - UUID)

UserOrg API (Router)
    ├─ /api/...        ← V1 endpoints (UUID-based)
    └─ /api/v2/...     ← V2 endpoints (Natural ID-based)
```

---

## 🏗️ Architecture

### Request Flow

```
External Client Request
    ↓
[Gateway (YARP)]
    ├─ Detects version from path
    ├─ Route-existence check (ADR-0106): 404 if no published route serves the path
    ├─ Authentication / authorization stage: 401, 403, or mint the internal token
    ├─ Routes to appropriate UserOrg endpoint
    └─ Proxies request through
    ↓
[UserOrg API]
    ├─ V1 Controllers: /api/projects/{id}        (UUID)
    ├─ V2 Controllers: /api/v2/Projects/{id}     (ShortName)
    └─ Responds with version-specific DTO
    ↓
Response (V1 or V2)
```

### Route Existence

The gateway's catch-all forwarder matches every path, so it has no unmatched-route state of its own. A
route-existence check runs **ahead of the auth stage** and decides only between a `404` and carrying on:

- The oracle is the published OpenAPI documents, `ApiDocuments.Published(isV1ApiEnabled)`: `Service`,
  `v2-User`, `v2-ProviderAdmin` and `v2-Admin` always, `User` and `Administration` only while `V1Api` is
  on. They are loaded from Core in the background and reconciled on an interval. It is
  deliberately **not** the RBAC endpoint map: a map miss means "no permission entry" and stays a
  `403 auth.no_permission_mapping`, which is how mapping gaps get noticed (ADR-0059).
- A path no document describes answers `404 application/problem+json` with code `route.not_found`, for
  anonymous and authenticated callers alike, and writes no audit record.
- It fails towards today's behaviour: an empty, stale or unloadable surface, a space no document described
  at all, and every path outside `/api` are left unjudged, so `401` and `403` keep their current meaning on
  every route that exists. With `V1Api` off the gateway reads no legacy
  document, so every gated legacy route answers `404`, the same status Core's `FeatureFlagMiddleware`
  gives it, while the ungated legacy endpoints (RemI, `api/version`, `api/DataStaging/*`, `api/Heappe/*`)
  stay routable through the `Service` document. The gateway reads the flag at startup, and both hosts must
  carry the same `FeatureManagement__V1Api`.

See [ADR-0106](../architecture/adr/0106-gateway-route-existence-oracle.md).

### Deployment Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    External Clients                          │
│  (Web, Mobile, Third-party Integrations)                     │
└──────────────────────────────┬───────────────────────────────┘
                               │
                    GET /projects/{id}
                    GET /v2/projects/{id}
                               │
                               ↓
┌──────────────────────────────────────────────────────────────┐
│                   Gateway Service (YARP)                     │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │  Route /projects/{id}      → UserOrg /api/projects/{id} │ │
│  │  Route /v2/projects/{id}   → UserOrg /api/v2/Projects/{id} │
│  │  Route /users/{id}         → UserOrg /api/users/{id}   │ │
│  │  Route /v2/users/{id}      → UserOrg /api/v2/Users/{id}│ │
│  └─────────────────────────────────────────────────────────┘ │
└──────────────────────────────┬───────────────────────────────┘
                               │
                               ↓
┌──────────────────────────────────────────────────────────────┐
│            UserOrg API Service                              │
│  ┌──────────────────────┐  ┌──────────────────────────────┐ │
│  │  V1 Endpoints        │  │  V2 Endpoints                │ │
│  │  /api/projects/{id}  │  │  /api/v2/Projects/{id}      │ │
│  │  /api/users/{id}     │  │  /api/v2/Users/{id}         │ │
│  │  [UUID-based]        │  │  [Natural ID-based]         │ │
│  │                      │  │                              │ │
│  │  Returns V1DTOs      │  │  Returns V2DTOs             │ │
│  │  (include Id)        │  │  (no Id field)              │ │
│  └──────────────────────┘  └──────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

---

## 🎯 Version Routing Rules

### Gateway Routes (YARP Configuration)

```
┌─ INCOMING ────────────────────┬─ GATEWAY ROUTING ──────────────────┬─ USERORG API ─────────────┐
│ GET /projects/{id}            │  /projects/{id}  (contains UUID)   │ → /api/projects/{id}       │
│ (UUID as id)                  │  Rule: Default route               │                            │
├───────────────────────────────┼────────────────────────────────────┼────────────────────────────┤
│ GET /v2/projects/{shortName}  │  /v2/projects/{shortName}          │ → /api/v2/Projects/{id}    │
│ (ShortName as id)             │  Rule: Prefix /v2                  │                            │
├───────────────────────────────┼────────────────────────────────────┼────────────────────────────┤
│ GET /users/{email}            │  /users/{email}  (contains Email)  │ → /api/users/{id}          │
│ (Email as id)                 │  Rule: Default route               │                            │
├───────────────────────────────┼────────────────────────────────────┼────────────────────────────┤
│ GET /v2/users/{email}         │  /v2/users/{email}                 │ → /api/v2/Users/{id}       │
│ (Email as id)                 │  Rule: Prefix /v2                  │                            │
└───────────────────────────────┴────────────────────────────────────┴────────────────────────────┘
```

### Endpoint Naming Convention

```
V1 (UUID-based):
  GET  /api/projects/{projectId}         ← UUID
  GET  /api/projects                     ← List
  POST /api/projects                     ← Create
  PUT  /api/projects/{projectId}         ← Update
  
V2 (Natural ID-based):
  GET  /api/v2/Projects/{projectShortName}   ← ShortName
  GET  /api/v2/Projects                      ← List
  POST /api/v2/Projects                      ← Create
  PUT  /api/v2/Projects/{projectShortName}   ← Update
```

---

## 🔧 Gateway Implementation (YARP)

### Gateway Configuration

File: `src/Modules/Services/Gateway/appsettings.json`

```json
{
  "ReverseProxy": {
    "Routes": {
      "api_v1_projects": {
        "ClusterId": "userorg_api",
        "Match": {
          "Path": "/api/projects{**catch-all}"
        },
        "Transforms": [
          {
            "PathRemovePrefix": "/api",
            "PathPrefix": "/api"
          }
        ]
      },
      "api_v2_projects": {
        "ClusterId": "userorg_api",
        "Match": {
          "Path": "/api/v2/projects{**catch-all}"
        },
        "Transforms": [
          {
            "PathPrefix": "/api"
          }
        ]
      },
      "api_v1_users": {
        "ClusterId": "userorg_api",
        "Match": {
          "Path": "/api/users{**catch-all}"
        },
        "Transforms": [
          {
            "PathRemovePrefix": "/api",
            "PathPrefix": "/api"
          }
        ]
      },
      "api_v2_users": {
        "ClusterId": "userorg_api",
        "Match": {
          "Path": "/api/v2/users{**catch-all}"
        },
        "Transforms": [
          {
            "PathPrefix": "/api"
          }
        ]
      }
    },
    "Clusters": {
      "userorg_api": {
        "Destinations": {
          "userorg_destination": {
            "Address": "https://userorg-api:5000"
          }
        }
      }
    }
  }
}
```

### Gateway Program Setup

File: `src/Modules/Services/Gateway/Program.cs`

```csharp
using Yarp.ReverseProxy.Configuration;

var builder = WebApplicationBuilder.CreateBuilder(args);

// Add YARP reverse proxy with versioning support
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms<VersionPrefixTransform>();

// Add versioning middleware for logging/tracking
builder.Services.AddScoped<IVersioningMiddleware, VersioningMiddleware>();

var app = builder.Build();

// Versioning middleware for request tracking
app.UseMiddleware<VersioningTrackingMiddleware>();

// YARP reverse proxy
app.MapReverseProxy();

app.Run();
```

### Version Prefix Transform (Custom YARP Transform)

File: `src/Modules/Services/Gateway/Transforms/VersionPrefixTransform.cs`

```csharp
using Yarp.ReverseProxy.Transforms;

namespace UserOrg.Gateway.Transforms;

/// <summary>
/// Custom YARP transform to handle version prefix routing
/// </summary>
public class VersionPrefixTransform : HttpTransformer
{
    private readonly ILogger<VersionPrefixTransform> _logger;

    public VersionPrefixTransform(ILogger<VersionPrefixTransform> logger)
    {
        _logger = logger;
    }

    public override async ValueTask TransformRequestAsync(
        HttpContext httpContext,
        HttpRequestMessage proxyRequest,
        string destinationPrefix)
    {
        var originalPath = httpContext.Request.Path.ToString();
        var version = ExtractVersion(originalPath);

        _logger.LogInformation(
            "Gateway routing - Original: {OriginalPath}, Version: {Version}, Destination: {Destination}",
            originalPath, version, destinationPrefix);

        // Track version in headers
        proxyRequest.Headers.Add("X-Client-Version", version);
        httpContext.Response.Headers.Add("X-API-Version", version);

        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix);
    }

    private static string ExtractVersion(string path)
    {
        return path.Contains("/v2/") ? "v2" : "v1";
    }
}
```

### Versioning Tracking Middleware

File: `src/Modules/Services/Gateway/Middleware/VersioningTrackingMiddleware.cs`

```csharp
namespace UserOrg.Gateway.Middleware;

/// <summary>
/// Middleware to track API version usage for monitoring
/// </summary>
public class VersioningTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<VersioningTrackingMiddleware> _logger;

    public VersioningTrackingMiddleware(
        RequestDelegate next,
        ILogger<VersioningTrackingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.ToString();
        var version = path.Contains("/v2/") ? "v2" : "v1";
        var endpoint = ExtractEndpoint(path);

        _logger.LogInformation(
            "API Request - Version: {Version}, Endpoint: {Endpoint}, Method: {Method}",
            version, endpoint, context.Request.Method);

        // Store in context for response tracking
        context.Items["ApiVersion"] = version;
        context.Items["Endpoint"] = endpoint;

        await _next(context);
    }

    private static string ExtractEndpoint(string path)
    {
        // Extract endpoint name from path
        // /api/projects/123 → projects
        // /api/v2/Projects/LEX → projects
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 1 ? segments[^2] : "unknown";
    }
}
```

---

## 💻 UserOrg API Implementation

### Separate Controllers for V1 and V2

#### V1 Projects Controller (UUID-based)

File: `src/Core/API/Controllers/V1/ProjectsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using UserOrg.Core.API.Dto.V1;

namespace UserOrg.Core.API.Controllers.V1;

/// <summary>
/// Projects API v1 - UUID-based identifiers (deprecated)
/// Endpoint: /api/projects
/// </summary>
[ApiController]
[Route("api/[controller]")]
[ApiExplorerSettings(GroupName = "v1")]
public class ProjectsController : ControllerBase
{
    private readonly IReadUserOrgDbContext _readContext;
    private readonly IWriteUserOrgDbContext _writeContext;
    private readonly IMapper _mapper;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IReadUserOrgDbContext readContext,
        IWriteUserOrgDbContext writeContext,
        IMapper mapper,
        ILogger<ProjectsController> logger)
    {
        _readContext = readContext;
        _writeContext = writeContext;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Get project by UUID
    /// </summary>
    /// <param name="projectId">Project UUID</param>
    /// <returns>Project data with Id field</returns>
    [HttpGet("{projectId}")]
    [ProducesResponseType(typeof(ProjectV1Dto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Obsolete("API v1 is deprecated. Use v2 (/api/v2/Projects/{shortName})")]
    public async Task<IActionResult> GetProject(string projectId)
    {
        try
        {
            // Parse UUID
            if (!ProjectId.TryParse(projectId, out var id))
            {
                _logger.LogWarning("Invalid project ID: {ProjectId}", projectId);
                return BadRequest(new { error = "Invalid project identifier" });
            }

            // Query by UUID (shadow property)
            var project = await _readContext.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => 
                    EF.Property<ProjectId>(p, "Id") == id);

            if (project == null)
            {
                return NotFound(new { error = "Project not found" });
            }

            var dto = _mapper.Map<ProjectV1Dto>(project);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving project {ProjectId}", projectId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// List all projects
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProjectV1Dto>), StatusCodes.Status200OK)]
    [Obsolete("API v1 is deprecated. Use v2 (/api/v2/Projects)")]
    public async Task<IActionResult> ListProjects()
    {
        var projects = await _readContext.Projects
            .AsNoTracking()
            .ToListAsync();

        var dtos = _mapper.Map<List<ProjectV1Dto>>(projects);
        return Ok(dtos);
    }

    /// <summary>
    /// Create new project
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectV1Dto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [Obsolete("API v1 is deprecated. Use v2 (/api/v2/Projects)")]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectV1Dto dto)
    {
        try
        {
            var shortNameResult = ProjectShortName.Create(dto.ShortName);
            if (!shortNameResult.IsSuccess)
            {
                return BadRequest(new { error = shortNameResult.Error });
            }

            var projectResult = Project.Create(
                dto.Name,
                shortNameResult.Value,
                dto.StartDate,
                dto.EndDate);

            if (!projectResult.IsSuccess)
            {
                return BadRequest(new { error = projectResult.Error });
            }

            await _writeContext.Projects.AddAsync(projectResult.Value);
            await _writeContext.SaveChangesAsync();

            var resultDto = _mapper.Map<ProjectV1Dto>(projectResult.Value);
            return CreatedAtAction(nameof(GetProject), 
                new { projectId = resultDto.Id }, resultDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating project");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

/// <summary>
/// DTO for creating project (V1)
/// </summary>
public record CreateProjectV1Dto(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate);
```

#### V2 Projects Controller (Natural ID-based)

File: `src/Core/API/Controllers/V2/ProjectsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using UserOrg.Core.API.Dto.V2;

namespace UserOrg.Core.API.Controllers.V2;

/// <summary>
/// Projects API v2 - Natural identifiers (recommended)
/// Endpoint: /api/v2/Projects
/// </summary>
[ApiController]
[Route("api/v2/[controller]")]
[ApiExplorerSettings(GroupName = "v2")]
public class ProjectsController : ControllerBase
{
    private readonly IReadUserOrgDbContext _readContext;
    private readonly IWriteUserOrgDbContext _writeContext;
    private readonly IMapper _mapper;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        IReadUserOrgDbContext readContext,
        IWriteUserOrgDbContext writeContext,
        IMapper mapper,
        ILogger<ProjectsController> logger)
    {
        _readContext = readContext;
        _writeContext = writeContext;
        _mapper = mapper;
        _logger = logger;
    }

    /// <summary>
    /// Get project by short name (natural identifier)
    /// </summary>
    /// <param name="projectShortName">Project short name (e.g., "LEX")</param>
    /// <returns>Project data without Id field</returns>
    [HttpGet("{projectShortName}")]
    [ProducesResponseType(typeof(ProjectV2Dto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProject(string projectShortName)
    {
        try
        {
            // Parse natural ID
            var shortNameResult = ProjectShortName.Create(projectShortName);
            if (!shortNameResult.IsSuccess)
            {
                _logger.LogWarning("Invalid project short name: {ShortName}", 
                    projectShortName);
                return BadRequest(new { error = "Invalid project identifier" });
            }

            // Query by natural ID (alternate key)
            var project = await _readContext.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ShortName == shortNameResult.Value);

            if (project == null)
            {
                return NotFound(new { error = "Project not found" });
            }

            var dto = _mapper.Map<ProjectV2Dto>(project);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving project {ShortName}", 
                projectShortName);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// List all projects
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProjectV2Dto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListProjects()
    {
        var projects = await _readContext.Projects
            .AsNoTracking()
            .ToListAsync();

        var dtos = _mapper.Map<List<ProjectV2Dto>>(projects);
        return Ok(dtos);
    }

    /// <summary>
    /// Create new project
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProjectV2Dto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectV2Dto dto)
    {
        try
        {
            var shortNameResult = ProjectShortName.Create(dto.ShortName);
            if (!shortNameResult.IsSuccess)
            {
                return BadRequest(new { error = shortNameResult.Error });
            }

            var projectResult = Project.Create(
                dto.Name,
                shortNameResult.Value,
                dto.StartDate,
                dto.EndDate);

            if (!projectResult.IsSuccess)
            {
                return BadRequest(new { error = projectResult.Error });
            }

            await _writeContext.Projects.AddAsync(projectResult.Value);
            await _writeContext.SaveChangesAsync();

            var resultDto = _mapper.Map<ProjectV2Dto>(projectResult.Value);
            return CreatedAtAction(nameof(GetProject), 
                new { projectShortName = resultDto.ShortName }, resultDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating project");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

/// <summary>
/// DTO for creating project (V2)
/// </summary>
public record CreateProjectV2Dto(
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate);
```

### Version-Specific DTOs

#### V1 DTO (with Id)

File: `src/Core/API/Dto/V1/ProjectV1Dto.cs`

```csharp
namespace UserOrg.Core.API.Dto.V1;

/// <summary>
/// V1 Project DTO - UUID-based (deprecated)
/// </summary>
[Obsolete("Use ProjectV2Dto instead. V1 is deprecated.")]
public record ProjectV1Dto(
    string Id,                      // ← INCLUDED in V1
    string Name,
    string ShortName,
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags);
```

#### V2 DTO (without Id)

File: `src/Core/API/Dto/V2/ProjectV2Dto.cs`

```csharp
namespace UserOrg.Core.API.Dto.V2;

/// <summary>
/// V2 Project DTO - Natural ID-based (recommended)
/// </summary>
public record ProjectV2Dto(
    string Name,
    string ShortName,              // ← Natural identifier
    DateTime StartDate,
    DateTime? EndDate,
    IReadOnlyList<string> Tags);
```

---

## 📊 OpenAPI Documentation

### Swagger Configuration for Versioning

File: `src/Core/API/Program.cs`

```csharp
using Microsoft.OpenApi.Models;

var builder = WebApplicationBuilder.CreateBuilder(args);

builder.Services
    .AddEndpointsApiExplorer()
    .AddSwaggerGen(options =>
    {
        // V1 Documentation
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "UserOrganization API - v1 (Deprecated)",
            Version = "1.0.0",
            Description = "Legacy UUID-based API. Use v2 instead.",
            Contact = new OpenApiContact
            {
                Name = "API Support",
                Url = new Uri("https://example.com/support")
            },
            Deprecated = true
        });

        // V2 Documentation
        options.SwaggerDoc("v2", new OpenApiInfo
        {
            Title = "UserOrganization API - v2 (Recommended)",
            Version = "2.0.0",
            Description = "Latest version using natural identifiers. Recommended for new clients.",
            Contact = new OpenApiContact
            {
                Name = "API Support",
                Url = new Uri("https://example.com/support")
            }
        });

        // Filter endpoints by API version
        options.DocInclusionPredicate((name, api) =>
        {
            if (api.GroupName == null)
                return false;

            return name.Equals(api.GroupName, StringComparison.OrdinalIgnoreCase);
        });

        // Add versioning info to endpoints
        options.OperationFilter<RemoveVersionFromParameterFilter>();
    })
    .AddScalarApiReference();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "swagger/{documentname}/swagger.json";
    })
    .UseScalarApiReference(options =>
    {
        options
            .WithTitle("UserOrganization API")
            .WithTheme(ScalarTheme.Default)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
            .AddServer(new ScalarServer
            {
                Url = "http://localhost:5000",
                Description = "Development"
            })
            .AddServer(new ScalarServer
            {
                Url = "https://api.example.com",
                Description = "Production"
            });
    });
}

app.MapControllers();
app.Run();
```

### Operation Filter for Documentation

File: `src/Core/API/Filters/RemoveVersionFromParameterFilter.cs`

```csharp
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UserOrg.Core.API.Filters;

public class RemoveVersionFromParameterFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Remove version group name from operation
        var versionParameter = operation.Parameters
            .FirstOrDefault(p => p.Name == "version");

        if (versionParameter != null)
        {
            operation.Parameters.Remove(versionParameter);
        }
    }
}
```

---

## 🎯 Request/Response Examples

### Example 1: Gateway Routing V1 (UUID)

**Client Request (via Gateway):**
```http
GET /projects/550e8400-e29b-41d4-a716-446655440000 HTTP/1.1
Host: api.example.com
```

**Gateway Routes To (UserOrg API):**
```http
GET /api/projects/550e8400-e29b-41d4-a716-446655440000 HTTP/1.1
Host: userorg-api:5000
X-Client-Version: v1
```

**Response (200 OK):**
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "LEXIS",
  "shortName": "LEX",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-12-31T23:59:59Z",
  "tags": ["important"]
}
```

---

### Example 2: Gateway Routing V2 (Natural ID)

**Client Request (via Gateway):**
```http
GET /v2/projects/LEX HTTP/1.1
Host: api.example.com
```

**Gateway Routes To (UserOrg API):**
```http
GET /api/v2/Projects/LEX HTTP/1.1
Host: userorg-api:5000
X-Client-Version: v2
```

**Response (200 OK):**
```json
{
  "name": "LEXIS",
  "shortName": "LEX",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-12-31T23:59:59Z",
  "tags": ["important"]
}
```

---

## 🔄 Gateway Configuration in Aspire

File: `src/UserOrg.AppHost/Program.cs`

```csharp
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// UserOrg Core API
var userOrgApi = builder
    .AddProject<Projects.UserOrg_Core_API>("userorg-api")
    .WithHttpEndpoint(port: 5001, targetPort: 5001, name: "http");

// Gateway with YARP
builder
    .AddProject<Projects.UserOrg_Gateway>("gateway")
    .WithHttpEndpoint(port: 5000, targetPort: 5000, name: "http")
    .WithReference(userOrgApi)
    .WaitFor(userOrgApi);

builder.Build().Run();
```

---

## 🧪 Testing

### Gateway Routing Tests

```csharp
[TestClass]
public class GatewayVersionRoutingTests
{
    private HttpClient _gatewayClient;
    private HttpClient _userOrgApiClient;
    private TestServer _gatewayServer;
    private TestServer _apiServer;

    [TestInitialize]
    public void Setup()
    {
        // Setup UserOrg API test server
        var apiBuilder = new WebHostBuilder().UseStartup<UserOrgApiStartup>();
        _apiServer = new TestServer(apiBuilder);
        _userOrgApiClient = _apiServer.CreateClient();

        // Setup Gateway test server
        var gatewayBuilder = new WebHostBuilder().UseStartup<GatewayStartup>();
        _gatewayServer = new TestServer(gatewayBuilder);
        _gatewayClient = _gatewayServer.CreateClient();
    }

    [TestMethod]
    public async Task GetProject_V1_RoutsToApiV1()
    {
        // Arrange
        var projectId = "550e8400-e29b-41d4-a716-446655440000";

        // Act
        var response = await _gatewayClient.GetAsync($"/projects/{projectId}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsAsync<JObject>();
        Assert.IsNotNull(json["id"]);  // V1 has id
        Assert.AreEqual(projectId, json["id"]);
    }

    [TestMethod]
    public async Task GetProject_V2_RoutsToApiV2()
    {
        // Arrange
        var shortName = "LEX";

        // Act
        var response = await _gatewayClient.GetAsync($"/v2/projects/{shortName}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsAsync<JObject>();
        Assert.IsNull(json["id"]);      // V2 no id
        Assert.AreEqual(shortName, json["shortName"]);
    }

    [TestMethod]
    public async Task GatewayAddsVersionHeader()
    {
        // Arrange
        var projectId = "550e8400-e29b-41d4-a716-446655440000";

        // Act
        var response = await _gatewayClient.GetAsync($"/projects/{projectId}");

        // Assert
        Assert.AreEqual("v1", response.Headers.GetValues("X-API-Version").First());
    }
}
```

---

## 📋 Checklist

### Gateway Setup
- [ ] YARP reverse proxy configured
- [ ] Routes for v1 and v2 defined
- [ ] Version prefix transform created
- [ ] Versioning tracking middleware added
- [ ] Headers forwarded correctly

### UserOrg API - V1
- [ ] V1 controllers created (/api/...)
- [ ] V1 DTOs created (with Id)
- [ ] UUID-based queries implemented
- [ ] Obsolete attributes added
- [ ] Swagger grouped as v1

### UserOrg API - V2
- [ ] V2 controllers created (/api/v2/...)
- [ ] V2 DTOs created (without Id)
- [ ] Natural ID queries implemented
- [ ] Swagger grouped as v2
- [ ] AutoMapper profiles configured

### Testing
- [ ] Gateway routing tests passing
- [ ] V1 endpoint tests passing
- [ ] V2 endpoint tests passing
- [ ] Version headers verified

### Documentation
- [ ] Swagger/OpenAPI specs created
- [ ] Scalar UI configured
- [ ] Client migration guide written
- [ ] Gateway routing documented

---

## 📚 Related Documentation

- **[📖 DDD Migration Documentation Index](./README.md)** - Complete reading guide and navigation
- **[DDD Migration Phase 6](./ddd-migration-phase6-api-versioning.md)** - API endpoint implementation with versioning
- **[Model Versioning Strategy](./model-versioning-evolution.md)** - How to evolve models without new endpoint versions
- **[Entity Relationship Complete Map](./entity-relationship-complete-map.md)** - All 24 entities and aggregates

---

## 🎯 Client Migration Path

### Phase 1: Coexistence (Week 1-4)
```
Gateway exposes:
  GET /projects/{id}     → v1 (UUID)
  GET /v2/projects/{id}  → v2 (Natural ID)

Both versions available
Clients can migrate at own pace
```

### Phase 2: v2 Recommended (Week 5-8)
```
Deprecation warnings on v1
v1 endpoints marked as obsolete
Support migrating clients
```

### Phase 3: v1 Sunset (Week 9+)
```
v1 endpoints removed
v2 becomes only option
```

---

**Document Version:** 2.0  
**Status:** ✅ Gateway-Based Path-Based Versioning  
**Last Updated:** 2026-04-24
