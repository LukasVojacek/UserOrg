# Waldur Data Export

Export UserOrg data back to Waldur platform to maintain synchronization.

## Overview

The Data Export component provides:
- Export UserOrg projects to Waldur
- Export user assignments
- Conflict resolution
- State management for reliable export

**Location:** `src/Modules/Integrations/Waldur/Adapters/`

## Architecture

### WaldurExporter
Main service for exporting data:

```csharp
public interface IWaldurExporter
{
    Task ExportProjectAsync(string projectId);
    Task ExportUserAssignmentAsync(string projectId, string userId);
    Task ExportAsync(ExportRequest request);
}
```

### WaldurExportOrchestrator
Coordinates export operations:

```csharp
public class WaldurExportOrchestrator
{
    public async Task ExecuteExportAsync(IEnumerable<Project> projects)
    {
        // Prepare exports
        // Execute in batches
        // Handle errors
        // Track state
    }
}
```

## Export Data Models

### ProjectExport
```csharp
public class ProjectExport
{
    public string ProjectId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ProviderId { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

### UserAssignmentExport
```csharp
public class UserAssignmentExport
{
    public string ProjectId { get; set; }
    public string UserId { get; set; }
    public string RoleId { get; set; }
    public DateTime AssignedAt { get; set; }
}
```

## What Gets Exported

### Projects
- Project name and description
- Project metadata
- Provider association
- Project status

### User Assignments
- User-to-project assignments
- User roles
- Assignment metadata
- Timestamp

### Resource Allocations
- Project resource assignments
- Allocation amounts
- Resource metadata

## Export Configuration

### In appsettings.json

```json
{
  "WaldurExport": {
    "Enabled": true,
    "BatchSize": 50,
    "RetryCount": 3,
    "SkipOnError": false,
    "LogExportDetails": true
  }
}
```

## Typical Export Flow

```
1. Collect changes from UserOrg
   ↓
2. Transform to Waldur format
   ↓
3. Validate data
   ↓
4. Check for conflicts
   ↓
5. Send to Waldur API
   ↓
6. Track export state
   ↓
7. Handle errors/retries
```

## Conflict Resolution

### Conflict Types

| Conflict | Resolution |
|----------|-----------|
| Project exists in both systems | Update existing project |
| Project only in UserOrg | Create new project in Waldur |
| Project only in Waldur | Skip (imported by sync) |
| User assignment differs | Use UserOrg as source of truth |
| Resource allocation differs | Update Waldur with UserOrg values |

### Resolution Strategy

```csharp
public enum ConflictResolution
{
    UserOrgWins,    // UserOrg is source of truth
    WaldurWins,     // Keep Waldur value
    Merge,          // Combine both
    Skip            // Don't export this item
}
```

## State Management

### Export State Tracking

```csharp
public class ExportState
{
    public string Id { get; set; }
    public string ProjectId { get; set; }
    public ExportStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ExportError> Errors { get; set; }
}

public enum ExportStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartiallyFailed
}
```

## Error Handling

### Errors and Retries

```csharp
public class ExportError
{
    public string Message { get; set; }
    public string ErrorCode { get; set; }
    public int RetryCount { get; set; }
    public bool IsRetryable { get; set; }
}
```

### Retry Strategy

- Transient errors: Retry 3 times with exponential backoff
- Non-transient errors: Log and skip
- Network errors: Retry with longer backoff

## Usage Example

```csharp
public class ProjectSyncService
{
    private readonly IWaldurExporter _exporter;
    
    public async Task ExportProjectChangesAsync()
    {
        // Get recently updated projects
        var changedProjects = await _projectRepository.GetChangedAsync();
        
        // Prepare export request
        var request = new ExportRequest
        {
            Projects = changedProjects,
            IncludeUsers = true,
            IncludeResources = true
        };
        
        // Execute export
        var result = await _exporter.ExportAsync(request);
        
        // Handle result
        if (result.IsSuccessful)
        {
            // Mark as exported
            await _projectRepository.MarkAsExportedAsync(changedProjects);
        }
    }
}
```

## Performance Considerations

### Batching

Exports are processed in batches:
- Default batch size: 50 items
- Configurable per request

### Throttling

API rate limiting is respected:
- Default: 10 requests/second
- Automatic request queueing

### Caching

Results are cached to avoid duplicate exports:
- Cache duration: 5 minutes
- Cache key: project ID + timestamp

## Monitoring & Logging

### Export Metrics

- Total exports attempted
- Successful exports
- Failed exports
- Average response time
- Retry counts

### Logging

All exports are logged with:
- Export ID
- Project/user ID
- Status
- Errors (if any)
- Timestamp

## Troubleshooting

### "Export failed" Error
- Check Waldur API availability
- Verify authentication token
- Check data validation errors

### "Conflict detected" Warning
- Review conflicting data
- Choose resolution strategy
- Re-run export

### "Timeout" Error
- Reduce batch size
- Check network connectivity
- Increase timeout configuration

## Status

✅ Fully implemented and production-ready

- Complete export pipeline
- Conflict resolution
- State tracking
- Error handling
- Performance optimized
