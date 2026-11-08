# Simplified Gateway Configuration

The Gateway configuration has been simplified to minimize required setup. Endpoints and caching settings are now **fixed in code**.

## ?? **Fixed Endpoints (Cannot be Changed)**

- **Discovery**: `/.well-known/openid-configuration`
- **JWKS**: `/.well-known/openid-configuration/certs`

## ?? **Fixed Settings**

- **JWKS Cache**: 1 hour (3600 seconds)
- **Discovery Cache**: 24 hours (86400 seconds)  
- **Certificate Chain**: Not included (minimal response size)
- **Certificate Thumbprint**: Included (for key identification)

## ?? **Required Configuration**

Only **1-2 settings** are configurable:

### Simple Approach (Issuer = Proxy URL)
```json
{
  "Gateway": {
    "Discovery": {
      "Issuer": "https://api.yourcompany.com"
    }
  }
}
```

### Flexible Approach (Separate Issuer & Proxy URL)
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

## ?? **Which Approach to Choose?**

### Use **Simple Approach** when:
- ? Proxy URL can be the JWT issuer identifier
- ? Infrastructure is stable (proxy URL won't change)
- ? You want minimal configuration

### Use **Flexible Approach** when:
- ? Need logical service name as issuer
- ? Proxy URL might change (different environments)
- ? Multiple proxies/load balancers
- ? Want separation of concerns

## ?? **Configuration Changes**

### ? **Removed from Configuration**
- `Gateway.Jwks.Enabled` - Always enabled
- `Gateway.Jwks.EndpointPath` - Fixed to `/certs`
- `Gateway.Jwks.CacheMaxAgeSeconds` - Fixed to 3600
- `Gateway.Jwks.IncludeCertificateChain` - Fixed to false
- `Gateway.Jwks.IncludeCertificateThumbprint` - Fixed to true
- `Gateway.Discovery.Enabled` - Always enabled  
- `Gateway.Discovery.EndpointPath` - Fixed to `/openid-configuration`
- `Gateway.Discovery.CacheMaxAgeSeconds` - Fixed to 86400

### ? **Still Configurable**
- `Gateway.Discovery.Issuer` - **REQUIRED**
- `Gateway.Discovery.PublicBaseUrl` - **OPTIONAL** (for proxy scenarios)
- `Gateway.Certificate.*` - All certificate settings remain configurable

## ?? **Benefits**

- **Minimal configuration** - Only 1 required field
- **Zero endpoint confusion** - Fixed, standard paths
- **Optimal performance** - Fixed caching settings
- **Reduced complexity** - Fewer configuration options to manage
- **Standard compliance** - Follows OpenID Connect conventions

## ?? **Migration from Old Configuration**

If you have existing configuration, you can remove all `Jwks` and `Discovery` fields except:
- Keep `Discovery.Issuer` 
- Keep `Discovery.PublicBaseUrl` (if using proxy)
- Remove everything else under `Jwks` and `Discovery`

## ?? **Example Responses**

### Discovery Response
```json
{
  "issuer": "https://your-gateway.com",
  "jwks_uri": "https://your-gateway.com/.well-known/openid-configuration/certs",
  "id_token_signing_alg_values_supported": ["RS256"]
}
```

### JWKS Response  
```json
{
  "keys": [{
    "kty": "RSA",
    "use": "sig",
    "kid": "abc123", 
    "alg": "RS256",
    "n": "base64url-modulus",
    "e": "AQAB",
    "x5t": "thumbprint"
  }]
}
```

All unnecessary OpenID Connect metadata has been removed for minimal, focused responses! ??