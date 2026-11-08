# Core API Component

Main business logic service. Manages projects, users, providers, locations, resources, and integrations with HEAppE and Waldur.

## Overview

The Core API contains:
- **Business logic** for all domain operations
- **API endpoints** for external and internal consumers
- **Application services** (User, Project, Provider, etc.)
- **Domain models** and persistence
- **Integration services** (HEAppE, Waldur adapters)

**Location:** `src/Core/API/API/`

## Core Services

See [Services Overview](services.md) for complete reference.

### User Management
- `IUserService` — Get/create/update users, manage preferences
- `IAdministrationService` — System-level operations

### Project Management
- `IProjectService` — CRUD operations, user assignments, resource assignments
- `IProjectTagService` — Project tagging
- `IProjectRequestService` — Project requests/approvals

### Resource Management
- `IProjectResourceService` — Resource CRUD
- `IProjectResourceAssignmentService` — Assignment management
- `ILocationService` — Location management

### Provider Management
- `IProviderService` — Provider CRUD operations

### Integration Services
- `IHeappeIntegrationRetrieverService` — Get cluster info, allocations, versions
- `IHeappeSynchronizationService` — Trigger HEAppE sync
- Waldur adapters — Data collection, sync, export

## API Endpoints

All endpoints are minimal APIs defined in `src/Core/API/API/`:

- `ProjectEndpoint.cs` — Project CRUD, user/resource assignments
- `UserEndpoint.cs` — User operations
- `ProviderEndpoint.cs` — Provider management
- `LocationEndpoint.cs` — Location management
- `ProjectResourceEndpoint.cs` — Resource CRUD
- `ProjectResourceAssignmentEndpoint.cs` — Assignment CRUD
- `ProjectTagEndpoint.cs` — Tagging operations
- `RequestsEndpoint.cs` — Request management
- `UserInfoEndpoint.cs` — User information and preferences
- `AdministrationEndpoint.cs` — Admin operations
- `HeappeEndpoint.cs` — HEAppE integration endpoints
- And 5+ more specialized endpoints

See [API Endpoints](endpoints.md) for detailed reference.

## Features

### [Read-Only Mode](features/read-only-mode.md)
Feature flag to disable all write endpoints, returning HTTP 404.

- Metadata + extension method: `.DisabledWhen("ReadOnlyMode")`
- Middleware enforcement
- Configuration via `appsettings.json` or environment

### [Hot-Path Caching](features/caching.md)
Performance optimization for frequently accessed endpoints.

- `GET /api/Project/TemplatesInfo/{projectShortName}` — Project templates (5 min TTL)
- `GET /api/Heappe/CommandTemplatePermissions/{HeappeIdentifier}` — Template permissions (5 min TTL)
- `GET /api/UserInfo` / `Extended` — User info (3 min TTL)

### [Endpoint Access Control](features/endpoint-access.md)
Fine-grained authorization via `IEndpointAccessService`.

- Per-endpoint permission checking
- RBAC integration with gateway
- Audit logging

## Data Model

See [Database Architecture](database.md) for:
- Entity Framework configuration
- Entity relationships
- Migration strategy
- Repository patterns

**Key Entities:**
- User, Project, Provider, Location
- ProjectResource, ProjectTag, ProjectRequest
- ActionLink, UserPermission, UserPreference
- And 15+ more domain entities

## Middleware & Cross-Cutting Concerns

- **FeatureFlagMiddleware** — Feature flag enforcement
- **GlobalExceptionHandler** — Centralized exception handling
- **OtelTraceResponseMiddleware** — Add trace IDs to responses
- **RequestSizeMiddleware** — Enforce request size limits
- **ValidationFilter** — FluentValidation integration

## OpenAPI Documentation

Scalar UI integration provides live API documentation:

- Interactive endpoint testing
- Security scheme configuration (Bearer tokens)
- Development only: an `Oidc` authorization-code login (PKCE) bound to the local Keycloak `testClient`, configured by `Scalar:Oidc` (`AuthorizationUrl`, `TokenUrl`, `ClientId`, `ClientSecret`); the AppHost sets it when the local Keycloak is the identity provider, and without it Scalar keeps the `Bearer` scheme
- Path-based document grouping
- PascalCase schema naming

## Authentication & Authorization

- Bearer token validation (JWT from Gateway)
- API key validation (service-to-service)
- Role-based access control (RBAC)
- Endpoint-level permission checking

## Health & Observability

- Custom health checks
- OpenTelemetry integration
- Structured logging
- Distributed tracing
- Telemetry for all major operations

## Status

✅ Fully implemented and production-ready

- 15+ endpoint groups
- 14+ application services
- Comprehensive domain model
- Full CRUD operations for all resources
- Integration with external systems (HEAppE, Waldur)
