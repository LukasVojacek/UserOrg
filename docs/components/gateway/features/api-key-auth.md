# API Key Authentication

Service-to-service authentication using API keys for secure inter-service communication.

## Overview

API Key Authentication provides:
- Secure service-to-service communication
- API key validation at Gateway
- Service identity verification
- Rate limiting and access control per service

## Architecture

### Two-Layer Approach

1. **Authentication Handler** — `ApiKeyAuthenticationHandler`
   - Intercepts requests with API key
   - Validates key format and existence
   - Creates authentication principal

2. **Validation Service** — `ServiceCallerValidator`
   - Verify service identity
   - Check if service has permission to call endpoint
   - Log access attempts

## Configuration

### API Keys Storage

Store API keys in Vault (via Aspire AppHost):

```yaml
Vault:
  ServiceKeys:
    heappe-service: "sk_heappe_xxxxxxxxxxxx"
    waldur-service: "sk_waldur_xxxxxxxxxxxx"
```

### In appsettings.json

```json
{
  "ServiceToService": {
    "Enabled": true,
    "ApiKeyHeader": "X-API-Key",
    "RequireHttps": true
  }
}
```

## Usage

### Client Side (Service Making Request)

```csharp
using var client = new HttpClient();
client.DefaultRequestHeaders.Add("X-API-Key", "sk_service_xxxxxxxxxxxx");

var response = await client.GetAsync("https://gateway/api/Project");
```

### Server Side (Gateway Validation)

```csharp
public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Extract API key from X-API-Key header
        // Validate against stored keys
        // Create authentication ticket
    }
}
```

## Service Validation

### IServiceCallerValidator

```csharp
public interface IServiceCallerValidator
{
    Task<bool> IsValidServiceCallerAsync(string serviceName, string endpoint);
}
```

### Usage in Endpoints

```csharp
app.MapPost("/api/internal/Project/Initialize", InitializeProjectHandler)
    .RequireServiceCaller("heappe-service")
    .WithName("InitializeProject");
```

## Configuration Options

| Option | Default | Description |
|--------|---------|-------------|
| `Enabled` | `true` | Enable/disable API key authentication |
| `ApiKeyHeader` | `X-API-Key` | Header name for API key |
| `RequireHttps` | `true` | Require HTTPS for API key requests |
| `CacheDurationMinutes` | `5` | Cache validation results |

## Security Best Practices

1. **Store keys securely** — Use Vault, not in code/config
2. **Rotate keys regularly** — Implement key rotation schedule
3. **Use HTTPS** — Enforce HTTPS for API key requests
4. **Log access** — Audit all API key usage
5. **Rate limiting** — Limit requests per API key
6. **Short-lived keys** — Consider expiration dates

## Common Services

| Service | Purpose | Endpoints |
|---------|---------|-----------|
| heappe-service | HEAppE cluster operations | `/api/Heappe/*` |
| waldur-service | Waldur synchronization | `/api/internal/Waldur/*` |
| admin-service | Administrative operations | `/api/Admin/*` |

## Troubleshooting

### "Invalid API Key"
- Verify key is correct
- Check key is not expired
- Confirm key is in Vault

### "Service not authorized"
- Check service permissions in configuration
- Verify endpoint exists
- Check service configuration

### "HTTPS required"
- Use HTTPS instead of HTTP
- Check `RequireHttps` configuration

## Status

✅ Fully implemented and production-ready

- Secure authentication mechanism
- Service-to-service validation
- Vault integration
- Comprehensive logging
