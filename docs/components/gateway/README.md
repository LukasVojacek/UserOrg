# Gateway Component

Reverse proxy, authentication, authorization, and routing orchestrator. YARP-based gateway that handles token validation, role-based access control, and service-to-service communication.

## Overview

The Gateway is the entry point to the system. It:
- Routes requests to downstream services (primarily the Core API)
- Validates JWT tokens and API keys
- Enforces role-based access control (RBAC)
- Provides token introspection endpoints
- Maintains OpenID Connect diagnostics
- Manages endpoint-level permissions

**Location:** `src/Modules/Services/Gateway/UserOrg.Gateway/`

## Key Features

### [Internal Token Management](features/internal-token/)
Generates and distributes JWT tokens. Provides JWKS (JSON Web Key Set) endpoint for token verification.

- `IInternalTokenProvider` — Token generation
- `TokenService` — Token lifecycle management
- `KeyService` — Cryptographic key management
- `JwksProvider` — Key distribution endpoint

### [Role-Based Access Control (RBAC)](features/role-management/)
Complete role and permission management system.

- `RoleManagementService` — Create, update, deactivate roles
- `RoleManagementState` — State persistence
- `RoleManagementCircuitGuard` — Safety circuit breaker
- UI components for role editor, permission selector
- Export (HTML, PDF) and import (JSON) capabilities

### [API Key Authentication](features/api-key-auth.md)
Validates API keys for service-to-service communication.

- `ApiKeyAuthenticationHandler` — API key validation

### [Bearer Token Validation](features/bearer-token-validation.md)
Validates the audience of user Bearer tokens against `Idp:Audience` and `Idp:AdditionalAudiences`.

- `FeatureManagement:StrictJwtAudience` — Strict audience check (default on)
- `StrictAudienceValidation` — Audience validator

### [Token Introspection](features/token-introspection.md)
Validates tokens and extracts metadata.

- `TokenIntrospectionService` — Token validation
- `ITokenIntrospectionService` — Interface

### [Endpoint Permission Mapping](features/role-management/)
Maps roles to API endpoints and enforces access control.

- `EndpointPermissionService` — Get/set/list endpoint permissions
- `PermissionMatcher` — Match endpoints to permissions
- `ServiceCallerValidator` — Validate inter-service calls

### [OIDC Diagnostics](features/diagnostics/)
Troubleshooting endpoint for OpenID Connect configuration issues.

- `OidcDiagnosticsEndpoint` — OIDC configuration diagnostics

### [Health Checks](features/health-checks.md)
Service health monitoring.

- `UserOrgApiHealthCheck` — Custom health check for Core API

## Configuration

See [Configuration Guide](Configuration-Guide.md) for:
- YARP routing rules
- Token settings
- Authentication configuration
- Endpoint access configuration
- Health check settings

## Certificate Implementation

See [Certificate Implementation](README-Certificate-Implementation.md) for TLS/SSL setup.

## Simplified Configuration

See [Simplified Config](SIMPLIFIED-CONFIG.md) for quick reference.

## Architecture Decisions

See [ADR-002: Reverse Proxy (YARP)](../architecture/adr/0002-reverse-proxy-yarp.md) for rationale.

## Typical Flow

1. **Request arrives** → Gateway receives HTTP request
2. **Token validation** → Extract and validate JWT from Authorization header
3. **Permission check** → Verify endpoint permissions via role-based access control
4. **Service-to-service validation** → Check API key for inter-service calls
5. **Routing** → Route to downstream service (typically Core API)
6. **Response** → Return response to client

## Integration Points

- **Core API** — Main business logic service
- **Keycloak** — OIDC provider (in Aspire AppHost)
- **Vault** — Secrets management (in Aspire AppHost)

## Status

✅ Fully implemented and production-ready

- Minimal API-based endpoints
- Blazor UI components for role management
- Comprehensive permission system
- Safety circuit breaker patterns
- Export/import capabilities
