# Forge.Core Library

Shared library for role management, permission services, import/export functionality, and endpoint mapping. Provides common abstractions used by Gateway and Core API.

## Overview

Forge.Core is a reusable component library that encapsulates:
- **Role Service** — Role lifecycle (create, update, get, list)
- **Permission Service** — Permission management and validation
- **Role List Service** — Role querying and filtering
- **Endpoint Permission Service** — Map roles to endpoints
- **UserOrg Roles Service** — System role definitions
- **Import/Export** — `IForgeImportExport` per feature, consumed by the Gateway role-management console
- **Endpoint Mapping** — Match endpoints to permissions

**Location:** `src/Modules/Libraries/Forge.Core/`

## Core Services

### Role Service
- `IRoleService` — Interface for role operations
- `RoleService` — Implementation
- **Operations:**
  - Create new role
  - Update existing role
  - Get role by ID
  - List all roles
  - Activate/deactivate role
  - Delete role

### Permission Service
- `IPermissionService` — Permission validation and management
- `PermissionService` — Implementation
- **Operations:**
  - Check if user has permission
  - Get permissions for role
  - Get permissions for user
  - Validate permission existence

### Role List Service
- `IRoleListService` — Role querying
- `RoleListService` — Implementation
- **Operations:**
  - Search roles by criteria
  - Filter roles by permission
  - Get roles for organization
  - Sort and paginate results

### Endpoint Permission Service
- `IEndpointPermissionService` — Map endpoints to permissions
- `EndpointPermissionService` — Implementation
- **Operations:**
  - Get permissions required by endpoint
  - Set permissions for endpoint
  - List all endpoint-permission mappings
  - Validate permission for endpoint

### UserOrg Roles Service
- `IUserorgRolesService` — System role definitions
- Predefined roles:
  - Administrator
  - Manager
  - User
  - And custom organization roles

### Import/Export (`IForgeImportExport`, consumed by the Gateway role-management console)
- `IForgeImportExport` — per-feature import/export (permissions, roles, endpoint mappings)
- **Formats:**
  - JSON for programmatic access
  - CSV for spreadsheet analysis
  - HTML for documentation
  - PDF for offline reference

**Operations:**
- Export role definitions
- Export permissions matrix
- Import role configurations
- Validate imported configurations

### Endpoint Mapping
- `IEndpointMapper` — Map endpoints to resources
- Permission matcher for role-based access
- Support for pattern-based endpoint matching

## Persistence

### ForgeDbContext
- Entity Framework context for Forge.Core entities
- Manages role, permission, and endpoint data
- Supports migrations

### Migrations
Create a new migration:
```bash
cd userorg/src/Modules/Libraries/Forge.Core
dotnet ef migrations add <NAME> -c ForgeDbContext -o Persistence/Migrations
```

## Entity Models

- **Role** — Role definition with name, description, status
- **Permission** — Permission with name, description, category
- **RolePermission** — Mapping between role and permission
- **Endpoint** — API endpoint definition
- **EndpointPermission** — Required permissions for endpoint

## Integration Points

- **Gateway** — Role management UI and endpoints
- **Core API** — Permission checking for endpoints
- **Blazor Components** — Role editor, permission selector, export UI

## Architecture

Forge.Core follows a service-oriented architecture:

1. **Domain Layer** — Role, Permission, Endpoint entities
2. **Application Layer** — Services for CRUD and business logic
3. **Persistence Layer** — Entity Framework data access
4. **No API Layer** — Forge.Core has no HTTP API; RBAC administration is the Gateway role-management console (see ADR-0117)

## Status

✅ Fully implemented and production-ready

- Complete role lifecycle management
- Fine-grained permission system
- Import/export capabilities
- Performance optimized queries
- Comprehensive error handling
