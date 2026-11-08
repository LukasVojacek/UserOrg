# ReadOnly Mode Feature Flag Implementation

## Overview

This implementation provides a centralized `ReadOnlyMode` feature flag that controls access to API write endpoints. When enabled, specified mutation endpoints return **HTTP 404 Not Found** instead of processing requests.

## Architecture

### Three-Layer Approach

1. **Metadata + Extension Method** (`DisabledByFeatureFlagMetadata.cs` + `Extensions.cs`)
   - Fluent API: `.DisabledWhen("ReadOnlyMode")` on endpoint registration
   - Stores feature flag name in endpoint metadata

2. **Middleware** (`FeatureFlagMiddleware.cs`)
   - Intercepts requests before routing
   - Reads metadata from endpoint
   - Checks `IFeatureManager.IsEnabledAsync(flagName)`
   - Returns **404 Not Found** if flag enabled

3. **Configuration** (`appsettings.json` / Environment Variables)
   - Feature flag stored in `FeatureManagement.ReadOnlyMode`
   - Set to `false` by default (writes enabled)
   - Can be overridden via environment variable `FeatureManagement__ReadOnlyMode=true`

## Configuration

### In appsettings.json

```json
{
  "FeatureManagement": {
    "ReadOnlyMode": false
  }
}
```

### Via Environment Variable (Production Override)

```bash
# Enable read-only mode
export FeatureManagement__ReadOnlyMode=true

# Or in Kubernetes
env:
  - name: FeatureManagement__ReadOnlyMode
    value: "true"
```

### Via Configuration Service

If using Azure App Configuration or similar, you can dynamically update the feature flag without redeploying.

## Protected Endpoints

The following write endpoints are protected with `.DisabledWhen("ReadOnlyMode")`:

### Request Workflow (Priority 1 - High)
- ✅ `POST /api/Requests` - Create request
- ✅ `PUT /api/Requests/{RequestId}/Accept` - Accept request
- ✅ `PUT /api/Requests/{requestId}/Reject` - Reject request

### Project Management (Priority 1-2 - High)
- ✅ `POST /api/Project` - Create project
- ✅ `PUT /api/Project` - Update project
- ✅ `DELETE /api/Project/{ProjectShortName}` - Delete project
- ✅ `PUT /api/Project/{ProjectShortName}/User/{IdpUserId}/Assign/{RoleId}` - Assign user role
- ✅ `PUT /api/Project/{ProjectShortName}/User/{IdpUserId}` - Add user to project
- ✅ `DELETE /api/Project/{ProjectShortName}/User/{IdpUserId}` - Remove user from project
- ✅ `PUT /api/Project/{ProjectShortName}/ProjectResource/{ProjectResourceName}` - Add resource to project
- ✅ `DELETE /api/Project/{ProjectShortName}/ProjectResource/{ProjectResourceName}` - Remove resource
- ✅ `PUT /api/Project/{ProjectShortName}/ProjectTag/{ProjectTagId}` - Add tag to project
- ✅ `DELETE /api/Project/{ProjectShortName}/ProjectTag/{ProjectTagId}` - Remove tag

### Project Resource Assignment (Priority 3 - Medium)
- ✅ `POST /api/ProjectResourceAssignment` - Create assignment
- ✅ `PUT /api/ProjectResourceAssignment` - Update assignment
- ✅ `DELETE /api/ProjectResourceAssignment/{Id}` - Delete assignment

### Resource Management (Priority 2-4 - Medium)
- ✅ `POST /api/ProjectResource` - Create project resource
- ✅ `PUT /api/ProjectResource` - Update project resource
- ✅ `DELETE /api/ProjectResource/{Id}` - Delete project resource
- ✅ `POST /api/Location` - Create location
- ✅ `PUT /api/Location` - Update location
- ✅ `DELETE /api/Location/{Id}` - Delete location
- ✅ `POST /api/Provider` - Create provider
- ✅ `PUT /api/Provider` - Update provider
- ✅ `DELETE /api/Provider/{Id}` - Delete provider

**Total Protected Endpoints: 26 write operations**

## Usage Example

### Register a Protected Endpoint

```csharp
// In your endpoint file (e.g., ProjectEndpoint.cs)

group.MapPost("/", async ([FromBody] ProjectCreate project, ...) =>
{
    // Create project logic
    return Results.CreatedAtRoute("GetProject", new { projectShortName = project.ShortName });

}).DisabledWhen("ReadOnlyMode")  // ← Add this line
  .Produces(StatusCodes.Status201Created);
```

### Test Read-Only Mode

```bash
# Default: writes allowed
curl -X POST https://api.example.com/api/Project \
  -H "Content-Type: application/json" \
  -d '{"shortName": "test", "description": "Test project"}'
# Returns: 201 Created

# Enable read-only mode via environment
export FeatureManagement__ReadOnlyMode=true

# Same request now returns:
curl -X POST https://api.example.com/api/Project \
  -H "Content-Type: application/json" \
  -d '{"shortName": "test", "description": "Test project"}'
# Returns: 404 Not Found
```

## How It Works

### Request Flow

```
1. Client sends POST /api/Project
           ↓
2. Middleware intercepts request
           ↓
3. Middleware reads endpoint metadata (DisabledByFeatureFlagMetadata)
           ↓
4. If metadata exists:
   - Check IFeatureManager.IsEnabledAsync("ReadOnlyMode")
   - If enabled → Return 404 Not Found ← SECURITY: Doesn't leak endpoint existence
   - If disabled → Continue to endpoint handler
           ↓
5. Endpoint processes request normally
```

### Security Notes

- **Returns 404, not 403**: Prevents leaking information about endpoint existence
- **Middleware runs early**: Before routing, prevents handler execution
- **No exception handling**: Flag check is synchronous and lightweight
- **Metadata-based**: Decoupled from business logic, purely infrastructural

## Files Modified/Created

### New Files
- `src/Core/API/FeatureManagement/DisabledByFeatureFlagMetadata.cs` - Metadata class
- `src/Core/API/Middleware/FeatureFlagMiddleware.cs` - Enforcement middleware
- `src/Core/API/OpenApi/FeatureFlagOperationTransformer.cs` - Marks disabled operations in OpenAPI spec
- `src/Core/API/OpenApi/FeatureFlagDocumentTransformer.cs` - Removes marked operations from spec
- `src/Core/API/OpenApi/FeatureFlagCleanupTransformer.cs` - Removes empty tags and paths from spec

### Modified Files
- `src/Core/API/Extensions.cs` - Added `DisabledWhen<T>()` extension method
- `src/Core/API/Program.cs` - Registered middleware and three OpenAPI transformers
- `appsettings.example.json` - Added `FeatureManagement.ReadOnlyMode` config
- `src/Core/API/API/*Endpoint.cs` (25 endpoints) - Added `.DisabledWhen("ReadOnlyMode")` to write endpoints

## OpenAPI Integration

When `ReadOnlyMode` feature flag is enabled, the **three-stage OpenAPI transformation pipeline** completely removes disabled operations from the generated specification:

### Three-Stage Transformation Pipeline

1. **FeatureFlagOperationTransformer** (Operation-level)
   - Reads endpoint metadata (`DisabledByFeatureFlagMetadata`)
   - Marks disabled operations with `[DISABLED]` prefix in description
   - Sets `Deprecated = true` on affected operations

2. **FeatureFlagDocumentTransformer** (Document-level)
   - Removes operations marked with `[DISABLED]` prefix
   - Leaves operation removal side effects (empty paths, orphaned tags)

3. **FeatureFlagCleanupTransformer** (Cleanup stage - runs LAST)
   - Removes empty tags (tags with no associated operations)
   - Removes empty paths (paths with no operations)
   - Ensures clean specification without artifacts

### Result

- ✅ **Operations are removed** - Write operations do not appear in OpenAPI schema when flag is enabled
- ✅ **Paths are cleaned up** - If all operations are removed from a path, the entire path is removed
- ✅ **Tags are cleaned up** - Empty tags do not appear in the specification
- ✅ **Clean API documentation** - Client tools (Scalar, Swagger UI) show only available endpoints
- ✅ **Consistent across specs** - Works reliably when switching between different API specifications (e.g., User vs Administration)

This provides the clearest indication that disabled endpoints are unavailable - they simply don't exist in the API specification when read-only mode is active.

## Future Enhancements

### Per-Endpoint Granularity (Not Implemented)
- Could extend to support per-endpoint flags instead of global
- Would require nested metadata structure
- Current approach: Single `ReadOnlyMode` flag for all write operations

### Audit Logging (Recommended)
- Add structured logging when flag-disabled requests are received
- Helps track when read-only mode was active
- Useful for debugging during maintenance windows

## Testing

### Manual Testing

```bash
# 1. Default state (writes allowed)
curl -X POST http://localhost:5000/api/Project ...  # → 201 Created

# 2. Enable read-only mode
export FeatureManagement__ReadOnlyMode=true

# 3. Verify writes blocked
curl -X POST http://localhost:5000/api/Project ...  # → 404 Not Found

# 4. Verify reads still work
curl -X GET http://localhost:5000/api/Project  # → 200 OK
```

### Automated Integration Tests

```csharp
[Fact]
public async Task WriteEndpoint_WhenReadOnlyModeEnabled_Returns404()
{
    // Enable read-only mode
    var options = Options.Create(new FeatureManagementOptions 
    { 
        FeatureFlags = new[] { new FeatureFlag { Name = "ReadOnlyMode", IsEnabled = true } }
    });

    var factory = new WebApplicationFactory<Program>();
    var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync("/api/Project", new { ... });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
}
```

## Security Considerations

✅ **Good Practices Implemented**
- Returns **404** to hide endpoint existence
- Middleware runs early to prevent handler execution
- Feature flag name stored in metadata (decoupled from routing)
- Immutable configuration (environment variables)

⚠️ **Additional Recommendations**
- Always use HTTPS in production
- Implement audit logging when flag is toggled
- Monitor for unexpected 404 patterns
- Regularly test read-only mode in staging

## References

- [Microsoft Feature Management Documentation](https://learn.microsoft.com/en-us/azure/azure-app-configuration/feature-management-dotnet-reference)
- [Azure App Configuration Integration](https://learn.microsoft.com/en-us/azure/azure-app-configuration/quickstart-dotnet-core-app)
- [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
