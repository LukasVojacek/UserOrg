# Endpoint Access Control

Fine-grained authorization system that controls access to specific API endpoints based on user permissions and roles.

## Overview

Endpoint Access Control provides a declarative way to:
- Define which permissions are required for each endpoint
- Validate user permissions before executing endpoint logic
- Audit access attempts
- Integrate with role-based access control (RBAC)

## Architecture

### Three-Layer Approach

1. **Metadata Layer** — `EndpointAccessConfiguration`
   - Stores permission requirements per endpoint
   - Defined at endpoint registration time

2. **Service Layer** — `IEndpointAccessService`
   - Query permission requirements
   - Validate user access
   - Cache permission checks

3. **Execution Layer** — Endpoint filters and middleware
   - Check permissions before handler execution
   - Return 403 Forbidden if access denied

## Core Components

### IEndpointAccessService
```csharp
public interface IEndpointAccessService
{
    // Get endpoints that require a specific permission
    Task<IEnumerable<string>> GetEndpointsForPermission(string permission);
    
    // Get permissions required by an endpoint
    Task<IEnumerable<string>> GetPermissionsForEndpoint(string endpoint);
    
    // Check if user has access to endpoint
    Task<bool> HasAccessToEndpoint(string userId, string endpoint);
    
    // Get all endpoint-permission mappings
    Task<Dictionary<string, List<string>>> GetEndpointPermissions();
}
```

### EndpointAccessExtensions
Register required permissions on endpoints:

```csharp
app.MapPost("/api/Project", CreateProjectHandler)
    .RequirePermission("project:create")
    .RequirePermission("project:view")
    .WithName("CreateProject");
```

## Usage Patterns

### Declarative Permission Requirements

```csharp
// Single permission
.RequirePermission("project:create")

// Multiple permissions (all required)
.RequirePermission("project:create")
.RequirePermission("project:edit")

// Custom validation
.WithEndpointAccess(context => {
    // Custom logic
    return context.User.HasClaim("custom", "value");
})
```

### Permission Checking at Runtime

```csharp
public class ProjectService
{
    private readonly IEndpointAccessService _accessService;
    
    public async Task<Project> CreateProjectAsync(CreateProjectRequest request)
    {
        // Validate access
        var hasAccess = await _accessService.HasAccessToEndpoint(
            userId: context.User.FindFirst("sub")?.Value,
            endpoint: "/api/Project"
        );
        
        if (!hasAccess)
            throw new UnauthorizedAccessException();
        
        // Create project...
    }
}
```

## Integration with RBAC

Endpoint Access Control integrates with the Gateway's role-based access control:

1. **Roles** are defined in Gateway
2. **Permissions** are assigned to roles
3. **Endpoints** require specific permissions
4. **Users** inherit permissions from roles

Flow:
```
User → Role → Permissions → Endpoint Access
```

## Configuration

### In appsettings.json

```json
{
  "EndpointAccess": {
    "Enabled": true,
    "CacheDurationMinutes": 5,
    "AuditLog": true
  }
}
```

### Environment Variables

```bash
EndpointAccess__Enabled=true
EndpointAccess__CacheDurationMinutes=5
```

## Audit Logging

All access checks can be logged:
- Timestamp
- User ID
- Endpoint
- Required permissions
- Access granted/denied

## Performance Considerations

- Permission checks are cached (default 5 minutes)
- Batch permission queries supported
- Early return on first missing permission
- No additional database round-trips if cached

## Common Permissions

| Permission | Applies To |
|-----------|-----------|
| `project:create` | POST /api/Project |
| `project:read` | GET /api/Project, GET /api/Project/{id} |
| `project:update` | PUT /api/Project |
| `project:delete` | DELETE /api/Project/{id} |
| `user:manage` | User management endpoints |
| `provider:manage` | Provider management endpoints |
| `admin:*` | All administrative endpoints |

## Status

✅ Fully implemented and production-ready

- Declarative permission model
- Efficient caching
- Comprehensive audit logging
- RBAC integration
