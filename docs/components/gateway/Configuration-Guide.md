# UserOrg Gateway Configuration Guide

This guide helps you configure the UserOrg Gateway for JWT token signing and validation with JWKS/Discovery endpoints.

## Quick Start

### 1. Minimal Configuration (Recommended for first time setup)

Edit your `appsettings.json`:

```json
{
  "Gateway": {
    "Discovery": {
      "Issuer": "https://your-gateway-url.com"
    }
  }
}
```

**That's it!** All other settings will use secure defaults.

### 2. Available Endpoints

After configuration, these endpoints will be available:

- **Discovery**: `GET /.well-known/openid-configuration`  
- **JWKS**: `GET /.well-known/openid-configuration/certs`

### 3. Example Response

**Discovery endpoint** (`/.well-known/openid-configuration`):
```json
{
  "issuer": "https://your-gateway-url.com",
  "jwks_uri": "https://your-gateway-url.com/.well-known/openid-configuration/certs", 
  "id_token_signing_alg_values_supported": ["RS256"]
}
```

**JWKS endpoint** (`/.well-known/openid-configuration/certs`):
```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig", 
      "kid": "abc123",
      "alg": "RS256",
      "n": "base64url-encoded-modulus",
      "e": "base64url-encoded-exponent"
    }
  ]
}
```

## Advanced Configuration

### Running Behind Proxy/Load Balancer

If your gateway runs behind a proxy (nginx, Cloudflare, etc.), configure the public URL:

```json
{
  "Gateway": {
    "Discovery": {
      "Issuer": "userorg-gateway",
      "PublicBaseUrl": "https://api.yourcompany.com"
    }
  }
}
```

### Development Environment

For local development with shorter certificate lifetimes:

```json
{
  "Gateway": {
    "Certificate": {
      "ValidityDays": 30,
      "PersistToDisk": false,
      "RotationCheckIntervalMinutes": 15
    },
    "Discovery": {
      "Issuer": "https://gateway.dev.localhost"
    }
  }
}
```

### Production Environment

For production with enhanced security:

```json
{
  "Gateway": {
    "Certificate": {
      "ValidityDays": 90,
      "StorageDirectory": "/app/certificates",
      "SubjectName": "CN=YourCompany Gateway Production"
    },
    "Jwks": {
      "IncludeCertificateChain": false
    },
    "Discovery": {
      "Issuer": "https://gateway.yourcompany.com"
    }
  }
}
```

## Configuration Reference

See `appsettings.example.json` for complete configuration examples and field descriptions.

### Required Settings

- **`Gateway.Discovery.Issuer`** - The public URL of your gateway (REQUIRED)

### Optional Settings

All other settings have secure defaults and can be omitted for basic usage.

## Client Integration

Clients can discover and validate JWT tokens using these endpoints:

```csharp
// 1. Discover configuration
var discovery = await httpClient.GetFromJsonAsync<dynamic>("https://gateway/.well-known/openid-configuration");

// 2. Get public keys  
var jwks = await httpClient.GetFromJsonAsync<JwksResponse>(discovery.jwks_uri.ToString());

// 3. Validate JWT tokens using the public keys
```

## Security Features

- ? **Automatic certificate generation** on first startup
- ? **Automatic certificate rotation** before expiration  
- ? **RSA-SHA256 signing** for enhanced security
- ? **Key identifiers** (`kid`) in JWT headers
- ? **Persistent certificate storage** (optional)
- ? **RFC 7517 compliant JWKS** 
- ? **HTTP caching headers** for performance

## Troubleshooting

### Gateway not generating certificates
- Check write permissions to certificate storage directory
- Review logs for certificate generation errors

### JWKS endpoint returning empty keys
- Verify certificate service is properly initialized
- Check if JWKS endpoint is enabled in configuration

### Clients can't validate tokens
- Confirm `issuer` in discovery matches JWT token issuer
- Verify `kid` in JWT header matches JWKS key ID
- Check that public key from JWKS can verify token signature

## Support

For more detailed information, see the complete documentation in `README-Certificate-Implementation.md`.