# Waldur Data Collection

REST API client for collecting data from Waldur platform.

## Overview

The Data Collection component provides:
- REST API client for Waldur
- Data model transformations
- Error handling and retry logic
- Rate limiting support

**Location:** `src/Modules/Integrations/Waldur/`

## Architecture

### CollectorService
Main service that fetches data from Waldur API:

```csharp
public interface ICollectorService
{
    Task<IEnumerable<ProviderDto>> GetProvidersAsync();
    Task<IEnumerable<ProjectDto>> GetProjectsAsync(string providerId);
    Task<IEnumerable<UserDto>> GetUsersAsync(string providerId);
    Task<IEnumerable<OfferingDto>> GetOfferingsAsync(string providerId);
}
```

## Configuration

### WaldurOptions

```csharp
public class WaldurOptions
{
    public string ApiUrl { get; set; }              // e.g., https://waldur.example.com/api
    public string ApiToken { get; set; }             // Authentication token
    public int PageSize { get; set; }               // Default: 100
    public TimeSpan Timeout { get; set; }           // Default: 30 seconds
    public int MaxRetries { get; set; }             // Default: 3
    public TimeSpan RetryDelay { get; set; }        // Default: 1 second
}
```

### In appsettings.json

```json
{
  "Waldur": {
    "ApiUrl": "https://waldur.example.com/api",
    "ApiToken": "token_from_vault",
    "PageSize": 100,
    "Timeout": "00:00:30",
    "MaxRetries": 3,
    "RateLimitPerSecond": 10
  }
}
```

## API Endpoints

### Providers
```
GET /api/customers/
```

Returns list of provider organizations.

**Response Model:**
```csharp
public class ProviderDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Abbreviation { get; set; }
    public string ContactPersons { get; set; }
}
```

### Projects
```
GET /api/projects/
```

Returns list of projects for a provider.

**Response Model:**
```csharp
public class ProjectDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string CustomerId { get; set; }  // Provider ID
    public DateTime CreatedAt { get; set; }
}
```

### Users
```
GET /api/users/
GET /api/projects/{projectId}/users/
```

Returns list of users in a project.

**Response Model:**
```csharp
public class UserDto
{
    public string Id { get; set; }
    public string Username { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public bool IsActive { get; set; }
}
```

### Offerings
```
GET /api/offerings/
```

Returns available offerings (resources).

**Response Model:**
```csharp
public class OfferingDto
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string ProviderId { get; set; }
}
```

## Authentication

API uses token-based authentication:

```
Authorization: Bearer <token>
```

Token is stored in Vault and passed via configuration.

## Error Handling

### Retry Strategy

Automatic retry on transient errors:

```csharp
public class RateLimitHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken cancellationToken)
    {
        int retries = 0;
        while (retries < _options.MaxRetries)
        {
            var response = await base.SendAsync(request, cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken);
                retries++;
                continue;
            }
            
            return response;
        }
    }
}
```

### Exception Handling

Common exceptions:
- `HttpRequestException` — Network error
- `TimeoutException` — Request timeout
- `JsonException` — Deserialization error
- `UnauthorizedAccessException` — Invalid token

## Data Models

### WaldurModels

Contains all DTO definitions:
- `ProviderDto`
- `ProjectDto`
- `UserDto`
- `OfferingDto`
- `OfferingComponentDto`
- `PriceDto`

### WaldurModelsExtensions

Transformation methods:

```csharp
public static class WaldurModelsExtensions
{
    public static ProviderModel ToProviderModel(this ProviderDto dto)
    {
        return new ProviderModel
        {
            Id = dto.Id,
            Name = dto.Name,
            // ... other mappings
        };
    }
}
```

## Usage Example

```csharp
public class WaldurDataCollectionService
{
    private readonly ICollectorService _collector;
    
    public async Task SyncDataAsync()
    {
        // Get all providers
        var providers = await _collector.GetProvidersAsync();
        
        foreach (var provider in providers)
        {
            // Get projects for provider
            var projects = await _collector.GetProjectsAsync(provider.Id);
            
            foreach (var project in projects)
            {
                // Get users in project
                var users = await _collector.GetUsersAsync(project.Id);
                
                // Process data...
            }
        }
    }
}
```

## Performance Considerations

### Pagination

All list endpoints support pagination:

```
GET /api/customers/?limit=100&offset=0
```

Default page size: 100 items

### Rate Limiting

Default rate limit: 10 requests/second

Exceeding limit returns HTTP 429 (Too Many Requests)

### Caching

Consider caching results to reduce API calls:
- Provider data: 1 hour TTL
- Project data: 30 minutes TTL
- User data: 15 minutes TTL

## Troubleshooting

### "Unauthorized" Response
- Verify API token is correct
- Check token is not expired
- Confirm token in Vault

### "Timeout" Errors
- Check network connectivity
- Increase `Timeout` configuration
- Check Waldur API availability

### "Too Many Requests"
- Reduce request rate
- Increase `RetryDelay`
- Implement request queuing

## Status

✅ Fully implemented and production-ready

- Complete REST API client
- All data models
- Error handling and retry
- Rate limiting support
