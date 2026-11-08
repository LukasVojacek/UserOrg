# Internal Token Feature

Complete feature for JWT token management with automatic RSA key rotation. Designed for inter-service communication with in-memory encryption certificate storage.

## 📋 Table of Contents

- [What is Internal Token Feature?](#what-is-internal-token-feature)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [API Reference](#api-reference)
- [Structure](#structure)

## What is Internal Token Feature?

Internal Token Feature provides:

- ✅ **JWT Token Generation** with digital signature
- ✅ **JWT Token Validation** for inter-service communication
- ✅ **Automatic RSA Key Rotation** with grace period
- ✅ **JWKS Endpoint** for public keys (`.well-known/jwks.json`)
- ✅ **In-memory Certificate Storage** with HybridCache
- ✅ **Fully Configurable System** without hard-coded values

## Prerequisites

- .NET 10.0 or higher
- `Microsoft.Extensions.Caching.Hybrid` package
- `System.IdentityModel.Tokens.Jwt` package
- ASP.NET Core 10.0+

## Installation

### Step 1: Copy Feature Folder

Copy the `Features/InternalToken` folder to your project:

```
YourProject/
├── Features/
│   └── InternalToken/
│       ├── IInternalTokenProvider.cs
│       ├── IJwksProvider.cs
│       ├── InternalTokenOptions.cs
│       ├── InternalTokenProvider.cs
│       ├── JwksProvider.cs
│       ├── KeyService.cs
│       ├── TokenService.cs
│       ├── InternalTokenFeatureExtensions.cs
│       ├── InternalTokenEndpointExtensions.cs
│       └── README.md (this file)
```

### Step 2: Register Feature in DI

In your `Program.cs` file:

```csharp
using YourProject.Features.InternalToken;

var builder = WebApplication.CreateBuilder(args);

// Add feature
builder.Services.AddHybridCache();
builder.Services.AddInternalTokenFeature(builder.Configuration);

// ... rest of configuration
```

### Step 3: Map Endpoints

In your `Program.cs` after `app.Build()`:

```csharp
var app = builder.Build();

// Map Internal Token endpoints (JWKS)
app.MapInternalTokenEndpoints();

// ... rest of mappings
```

## Configuration

Add to `appsettings.json`:

```json
{
  "InternalToken": {
    "Issuer": "http://localhost:5134",
    "Audience": "http://localhost:5049",
    "ExpirationMinutes": 60,
    "KeyRotationIntervalHours": 24,
    "KeyGracePeriodHours": 48,
    "JwksCacheExpirationSeconds": 30
  }
}
```

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Issuer` | string | `https://localhost:5001` | JWT issuer URL |
| `Audience` | string | `https://localhost:5002` | JWT audience URL |
| `ExpirationMinutes` | int | `60` | Token expiration time in minutes |
| `KeyRotationIntervalHours` | int | `24` | Key rotation interval in hours |
| `KeyGracePeriodHours` | int | `48` | Grace period for old keys in hours |
| `JwksCacheExpirationSeconds` | int | `30` | JWKS JSON cache expiration in seconds |

> **Note**: If the `InternalToken` section is missing from `appsettings.json`, default values will be used.

## Usage

### Generate Token

```csharp
public class MyService
{
    private readonly IInternalTokenProvider _tokenProvider;

    public MyService(IInternalTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public async Task<string> GetTokenAsync(string username)
    {
        var token = await _tokenProvider.GenerateInternalTokenAsync(
            username: username,
            email: "user@example.com",
            roles: new[] { "admin", "user" }
        );
        return token;
    }
}
```

### Validate Token

```csharp
var principal = await _tokenProvider.ValidateInternalTokenAsync(token);

if (principal != null)
{
    var username = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var roles = principal.FindAll(ClaimTypes.Role);
    // Token is valid
}
else
{
    // Token is invalid
}
```

### Get Current Key ID

```csharp
var keyId = _tokenProvider.GetCurrentKeyId();
// Returns the ID of the current RSA key used for signing
```

### JWKS Endpoint

Automatically available at: `GET /.well-known/jwks.json`

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "a1b2c3d4e5f6...",
      "alg": "RS256",
      "n": "xjlCRBqkQfXYWe...",
      "e": "AQAB"
    }
  ]
}
```

## API Reference

### IInternalTokenProvider

```csharp
public interface IInternalTokenProvider
{
    /// <summary>
    /// Generates an internal JWT token for inter-service communication.
    /// </summary>
    Task<string> GenerateInternalTokenAsync(
        string username,
        string? email = null,
        IEnumerable<string>? roles = null);

    /// <summary>
    /// Validates an internal JWT token.
    /// </summary>
    Task<ClaimsPrincipal?> ValidateInternalTokenAsync(string token);

    /// <summary>
    /// Returns the ID of the current key used for signing.
    /// </summary>
    string GetCurrentKeyId();
}
```

### IJwksProvider

```csharp
public interface IJwksProvider
{
    /// <summary>
    /// Returns JWKS JSON from cache.
    /// </summary>
    Task<string> GetCachedJwksJsonAsync();

    /// <summary>
    /// Returns the complete JWKS object.
    /// </summary>
    JsonWebKeySet GetJsonWebKeySet();
}
```

### InternalTokenOptions

```csharp
public sealed class InternalTokenOptions
{
    public const string SectionName = "InternalToken";
    
    public string Issuer { get; set; } = "https://localhost:5001";
    public string Audience { get; set; } = "https://localhost:5002";
    public int ExpirationMinutes { get; set; } = 60;
    public int KeyRotationIntervalHours { get; set; } = 24;
    public int KeyGracePeriodHours { get; set; } = 48;
    public int JwksCacheExpirationSeconds { get; set; } = 30;
    
    public void Validate();
}
```

## Structure

```
Features/InternalToken/
├── IInternalTokenProvider.cs           # Public interface for token generation/validation
├── IJwksProvider.cs                    # Public interface for JWKS
├── InternalTokenOptions.cs             # Public configuration options
├── InternalTokenProvider.cs            # Internal implementation of IInternalTokenProvider
├── JwksProvider.cs                     # Internal implementation of IJwksProvider
├── KeyService.cs                       # Internal RSA key management and rotation
├── TokenService.cs                     # Internal JWT generation and validation
├── InternalTokenFeatureExtensions.cs   # Public DI extension methods
├── InternalTokenEndpointExtensions.cs  # Public endpoint extension methods
└── README.md                           # This file
```

### Visibility

**Public (to be exported in NuGet package):**
- `IInternalTokenProvider` - Interface for end-users
- `IJwksProvider` - Interface for end-users
- `InternalTokenOptions` - Configuration
- `InternalTokenFeatureExtensions` - DI registration
- `InternalTokenEndpointExtensions` - Endpoint registration

**Internal (hidden in implementation):**
- `InternalTokenProvider` - Implementation
- `JwksProvider` - Implementation
- `KeyService` - System logic
- `TokenService` - System logic

## Advanced Usage

### Custom Configuration in Code

If you want to override configuration without `appsettings.json`:

```csharp
services.AddOptions<InternalTokenOptions>()
    .Configure(options =>
    {
        options.Issuer = "https://my-service.com";
        options.Audience = "https://other-service.com";
        options.ExpirationMinutes = 120;
    })
    .ValidateOnStart();
```

### Key Monitoring

Keys are automatically rotated based on `KeyRotationIntervalHours`. Old keys remain available for `KeyGracePeriodHours` to allow validation of older tokens.

### Performance

- **HybridCache** - Caching JWKS JSON and RSA parameters
- **In-memory Storage** - RSA keys are kept in memory for speed
- **Automatic Cleanup** - Old keys are automatically deleted after grace period

## Troubleshooting

### Problem: "InternalToken section not found in configuration"

**Solution**: Verify that you have the `InternalToken` section in your `appsettings.json`:

```json
{
  "InternalToken": { ... }
}
```

### Problem: Token validation fails

**Solution**: Verify:
1. Issuer and Audience match in both services
2. Key is not in grace period
3. Token is not expired

### Problem: JWKS endpoint returns 404

**Solution**: Verify that you called `app.MapInternalTokenEndpoints()` in `Program.cs`

## License

This feature is part of the SignedJWTExample project.

## Changelog

### v1.0.0 (2025-12-18)
- ✅ Initial release
- ✅ C# 14 best practices
- ✅ Full configurability
- ✅ Automatic key rotation
- ✅ JWKS endpoint
