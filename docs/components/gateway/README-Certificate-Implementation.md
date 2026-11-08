# Certificate-Based Internal Token Service with JWKS and JWT Validation Discovery Support

This implementation replaces the previous HMAC-based JWT signing with RSA certificate-based signing for enhanced security and adds RFC 7517 compliant JWKS endpoint plus a minimal JWT validation discovery endpoint for internal services to validate tokens.

## Key Features

### Certificate Management
- **Automatic Generation**: Certificates are generated automatically on first startup
- **Self-Signed**: Uses self-signed X.509 certificates for internal token signing
- **Automatic Rotation**: Certificates are automatically rotated before expiration
- **Persistent Storage**: Certificates can be persisted to disk for restart durability
- **Configurable Validity**: Certificate validity periods and rotation thresholds are configurable

### Security Enhancements
- **RSA-SHA256**: Uses RSA-SHA256 algorithm instead of HMAC-SHA256
- **Key Rotation**: Regular automatic certificate rotation reduces exposure
- **Certificate Validation**: Full certificate validation including expiration checks
- **Key Identifiers**: JWT tokens include `kid` header for proper key identification

### JWKS Endpoint
- **RFC 7517 Compliance**: Full compliance with JSON Web Key Set standard
- **Minimal API**: Implemented using .NET 10 Minimal API with REPR pattern
- **Public Key Distribution**: Clients can retrieve public keys for JWT verification
- **Caching Support**: Proper HTTP caching headers for performance
- **Filtering Support**: Optional filtering by key ID and usage type

### JWT Validation Discovery Endpoint
- **Minimal Configuration**: Only provides essential information for JWT token validation
- **Internal Services Focus**: Designed specifically for internal service-to-service communication
- **Dynamic Metadata**: Generates metadata directly from token service to ensure consistency
- **Minimal API**: Implemented using .NET 10 Minimal API with REPR pattern
- **Essential Metadata**: Issuer, JWKS URI, signing algorithms, and audience information
- **No OIDC Overhead**: Excludes unnecessary OpenID Connect fields not needed for token validation

### Configuration

#### Production Settings (`appsettings.json`)
```json
{
  "Gateway": {
    "Certificate": {
      "ValidityDays": 90,
      "RotationThresholdDays": 7,
      "KeySize": 2048,
      "SubjectName": "CN=UserOrg Gateway Internal",
      "StorageDirectory": "certificates",
      "CertificateFileName": "gateway-internal",
      "PersistToDisk": true,
      "RotationCheckIntervalMinutes": 60
    },
    "Jwks": {
      "Enabled": true,
      "EndpointPath": ".well-known/jwks",
      "CacheMaxAgeSeconds": 3600,
      "IncludeCertificateChain": true,
      "IncludeCertificateThumbprint": true
    },
    "Discovery": {
      "Enabled": true,
      "EndpointPath": ".well-known/jwt-validation",
      "Issuer": "https://gateway.example.com",
      "PublicBaseUrl": null,
      "UseRequestUrlForJwksUri": false,
      "CacheMaxAgeSeconds": 86400
    }
  }
}
```

#### Development Settings (`appsettings.Development.json`)
```json
{
  "Gateway": {
    "Certificate": {
      "ValidityDays": 30,
      "RotationThresholdDays": 3,
      "StorageDirectory": "certificates-dev",
      "RotationCheckIntervalMinutes": 15
    },
    "Jwks": {
      "CacheMaxAgeSeconds": 1800
    },
    "Discovery": {
      "Issuer": "https://gateway.dev.localhost",
      "CacheMaxAgeSeconds": 3600
    }
  }
}
```

### Services

- **ICertificateService**: Interface for certificate management operations
- **CertificateService**: Implementation of certificate generation, storage, and rotation
- **IJwksService**: Interface for JWKS generation and management
- **JwksService**: Implementation of RFC 7517 compliant JWKS generation
- **IDiscoveryEndpoint**: Interface for JWT validation discovery endpoint
- **DiscoveryEndpoint**: Implementation of minimal validation metadata generation
- **CertificateRotationService**: Background service for periodic certificate checks
- **InternalTokenService**: Updated to use certificate-based signing with key identifiers

### JWKS Endpoint (Minimal API + REPR Pattern)

- **IJwksEndpoint**: REPR pattern endpoint interface
- **JwksEndpoint**: REPR pattern endpoint implementation
- **JwksRequest**: Request representation with optional filtering
- **JwksResponse**: Response representation following RFC 7517

#### Endpoint Details
- **URL**: `/.well-known/jwks` (configurable)
- **Method**: `GET`
- **Query Parameters**: 
  - `kid` (optional): Filter by key identifier
  - `use` (optional): Filter by key usage (e.g., "sig")
- **Response Format**: RFC 7517 compliant JSON Web Key Set

#### Example Response
```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "kid": "abc123",
      "alg": "RS256",
      "n": "base64url-encoded-modulus",
      "e": "base64url-encoded-exponent",
      "x5t": "certificate-thumbprint",
      "x5c": ["base64-encoded-certificate"]
    }
  ]
}
```

### JWT Validation Discovery Endpoint (Minimal API + REPR Pattern)

- **IDiscoveryEndpoint**: REPR pattern endpoint interface
- **DiscoveryEndpoint**: REPR pattern endpoint implementation using IInternalTokenService
- **DiscoveryRequest**: Request representation with optional format parameter
- **DiscoveryResponse**: Response representation for JWT validation metadata

#### Key Features
- **Service-Driven Metadata**: Generates metadata directly from `IInternalTokenService` to ensure consistency
- **No Configuration Duplication**: Eliminates separate configuration classes by using actual service values
- **Runtime Accuracy**: Always reflects current token service behavior
- **Proxy Support**: Multiple options for configuring JWKS URI in proxy/load balancer scenarios

#### Proxy Configuration Options

1. **Fixed Public URL** - Set `PublicBaseUrl` in configuration:
```json
{
  "Gateway": {
    "Discovery": {
      "Issuer": "userorg-gateway",
      "PublicBaseUrl": "https://public-proxy.example.com"
    }
  }
}
```

2. **Request-Based URL** - Use incoming request URL:
```json
{
  "Gateway": {
    "Discovery": {
      "UseRequestUrlForJwksUri": true
    }
  }
}
```

3. **Configuration Priority**:
   - If `UseRequestUrlForJwksUri = true` ? uses request scheme + host
   - If `PublicBaseUrl` is set ? uses PublicBaseUrl
   - Otherwise ? uses `Issuer` value

#### Endpoint Details
- **URL**: `/.well-known/jwt-validation` (configurable)
- **Method**: `GET`
- **Query Parameters**: 
  - `format` (optional): Response format (defaults to JSON)
- **Response Format**: Minimal JSON with only JWT validation essentials

#### Example Response (Generated from Token Service)
```json
{
  "issuer": "userorg-gateway",
  "jwks_uri": "https://gateway.example.com/.well-known/jwks",
  "id_token_signing_alg_values_supported": ["RS256"],
  "audience": "internal",
  "token_lifetime_seconds": 300
}
```

### Background Services

The `CertificateRotationService` runs in the background and:
1. Initializes certificates on application startup
2. Periodically checks certificate expiration
3. Automatically rotates certificates when needed
4. Logs all certificate operations

### Client Integration

Clients can now:
1. **Discover JWT Validation Info**: GET `/.well-known/jwt-validation` for validation metadata
2. **Retrieve Public Keys**: GET `/.well-known/jwks` to get current public keys
3. **Verify JWT Tokens**: Use the public key matching the `kid` in JWT header
4. **Handle Key Rotation**: Automatically fetch new keys when `kid` changes
5. **Cache Configuration**: Respect HTTP caching headers for performance

### Migration Notes

The following changes were made during migration:
- Removed dependency on `Gateway:InternalSigningKey` configuration
- Removed secret parameter from Aspire AppHost
- Added certificate storage directory (defaults to `certificates/`)
- Updated from HMAC-SHA256 to RSA-SHA256 signing
- Added automatic certificate lifecycle management
- Replaced Controller-based JWKS endpoint with Minimal API + REPR pattern
- Added key identifiers (`kid`) to JWT tokens
- Added minimal JWT validation discovery endpoint for internal services

### File Structure

```
UserOrg.Gateway/
??? Services/
?   ??? ICertificateService.cs          # Certificate management interface
?   ??? CertificateService.cs           # Certificate implementation
?   ??? IJwksService.cs                 # JWKS service interface
?   ??? JwksService.cs                  # JWKS implementation
?   ??? InternalTokenService.cs         # Updated token service
??? Endpoints/
?   ??? Jwks/
?   ?   ??? IJwksEndpoint.cs            # JWKS endpoint interface
?   ?   ??? JwksEndpoint.cs             # JWKS endpoint implementation
?   ?   ??? JwksRequest.cs              # Request representation
?   ?   ??? JwksResponse.cs             # Response representation
?   ??? Discovery/
?       ??? IDiscoveryEndpoint.cs       # Discovery endpoint interface
?       ??? DiscoveryEndpoint.cs        # Discovery endpoint implementation
?       ??? DiscoveryRequest.cs         # Request representation
?       ??? DiscoveryResponse.cs        # Response representation
?       ??? OpenIdConnectConfiguration.cs # Discovery configuration model
??? Extensions/
?   ??? JwksEndpointExtensions.cs       # Minimal API endpoint registration
??? Configuration/
?   ??? CertificateOptions.cs           # Certificate configuration
?   ??? JwksOptions.cs                  # JWKS configuration
?   ??? DiscoveryOptions.cs             # Discovery configuration
??? HostedServices/
?   ??? CertificateRotationService.cs   # Background rotation service
??? certificates/                       # Certificate storage (created at runtime)
    ??? gateway-internal.pfx             # Current certificate
```

### Runtime Behavior

1. **Startup**: Application initializes certificate service and generates/loads certificate
2. **Token Creation**: Each internal token is signed with the current certificate using RSA-SHA256 and includes `kid` header
3. **Discovery Endpoint**: Serves JWT validation metadata at `/.well-known/jwt-validation`
4. **JWKS Endpoint**: Serves public keys at `/.well-known/jwks` for client verification
5. **Background Rotation**: Service checks every hour (configurable) for certificate rotation needs
6. **Automatic Recovery**: If certificate is corrupted or expired, a new one is generated automatically

### Example Client Usage

```csharp
// 1. Discover JWT validation configuration (generated dynamically from token service)
var httpClient = new HttpClient();
var validation = await httpClient.GetFromJsonAsync<dynamic>("https://gateway/.well-known/jwt-validation");

// 2. Fetch JWKS from the discovered endpoint
var jwks = await httpClient.GetFromJsonAsync<JwksResponse>(validation.jwks_uri.ToString());

// 3. Find key by kid from JWT header
var jwt = "eyJ..."; // JWT token with kid in header
var handler = new JwtSecurityTokenHandler();
var token = handler.ReadJwtToken(jwt);
var kid = token.Header["kid"].ToString();
var key = jwks.Keys.FirstOrDefault(k => k.KeyId == kid);

// 4. Verify token using public key and configuration
// - Check issuer matches validation.issuer
// - Check audience matches validation.audience (if specified)
// - Verify signature using the public key
```