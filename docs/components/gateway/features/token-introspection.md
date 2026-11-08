# Token Introspection

Service for validating JWT tokens and extracting metadata.

## Overview

Token Introspection provides:
- JWT token validation
- Signature verification
- Claims extraction
- Token expiration checking
- Caching of introspection results

## Architecture

### ITokenIntrospectionService

```csharp
public interface ITokenIntrospectionService
{
    Task<TokenIntrospectionResult> IntrospectAsync(string token);
    Task<bool> IsValidAsync(string token);
    Task<Dictionary<string, object>> GetClaimsAsync(string token);
}
```

### TokenIntrospectionService

Main implementation that:
1. Extracts token from Authorization header
2. Validates JWT signature
3. Checks token expiration
4. Extracts and validates claims
5. Caches results

## Token Format

Expected JWT structure:

```
Header: { "alg": "RS256", "typ": "JWT" }
Payload: { "sub": "user-id", "exp": 1234567890, ... }
Signature: <calculated using private key>
```

## Usage

### In Endpoints

```csharp
public class ProjectHandler
{
    private readonly ITokenIntrospectionService _tokenIntrospection;
    
    public async Task<IResult> GetAsync(HttpContext context)
    {
        var authHeader = context.Request.Headers["Authorization"].ToString();
        var token = authHeader.Replace("Bearer ", "");
        
        var isValid = await _tokenIntrospection.IsValidAsync(token);
        if (!isValid)
            return Results.Unauthorized();
        
        var claims = await _tokenIntrospection.GetClaimsAsync(token);
        var userId = claims["sub"]?.ToString();
        
        // Use userId...
    }
}
```

### JWKS Endpoint

The JWKS (JSON Web Key Set) endpoint provides public keys for token verification:

```
GET /api/jwks
```

Response:
```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "key-id-1",
      "n": "...",
      "e": "AQAB"
    }
  ]
}
```

## Configuration

### In appsettings.json

```json
{
  "TokenIntrospection": {
    "Enabled": true,
    "CacheDurationMinutes": 5,
    "ValidateExpiration": true,
    "ValidateSignature": true,
    "JwksUrl": "https://keycloak/jwks",
    "Issuer": "https://keycloak/issuer"
  }
}
```

## Performance

- Introspection results are cached (default 5 minutes)
- JWKS keys are cached for performance
- Early return on invalid signature

## Common Claims

| Claim | Description |
|-------|-------------|
| `sub` | Subject (user ID) |
| `exp` | Expiration time (Unix timestamp) |
| `iat` | Issued at (Unix timestamp) |
| `iss` | Issuer |
| `aud` | Audience |
| `roles` | User roles |
| `permissions` | User permissions |

## Error Cases

| Error | Cause | Solution |
|-------|-------|----------|
| Invalid signature | Token modified or wrong key | Get new token |
| Token expired | `exp` claim passed current time | Refresh token |
| Missing required claims | Token incomplete | Issue new token |
| JWKS unavailable | Cannot download public keys | Check network connectivity |

## Status

✅ Fully implemented and production-ready

- JWT validation
- Signature verification
- Efficient caching
- Comprehensive error handling
