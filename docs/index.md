# UserOrg Documentation

Complete documentation for UserOrganization — a distributed .NET Aspire application for managing research organization resources, users, and integrations with HEAppE and Waldur.

> **Quick Navigation:** [Architecture](#architecture) • [Components](#components) • [Integration Guides](#integrations) • [Development](#development)

---

## Getting Started

- [**Team Onboarding Guide**](ARCHITECTURE_GUIDE.md) — Quick reference for developers (file locations, naming conventions, module structure)
- [**Refactoring Plan**](REFACTORING_PLAN.md) — Multi-phase refactoring roadmap (DDD migration, API versioning)

---

## Architecture

- [**Architecture Overview**](architecture/index.md) — System design and patterns
- [**Architecture Decision Records (ADRs)**](architecture/adr/) — Major technical decisions
  - [ADR-001: Gateway Internal Token](architecture/adr/0001-gateway-internal-token.md)
  - [ADR-002: Reverse Proxy (YARP)](architecture/adr/0002-reverse-proxy-yarp.md)
  - [ADR-003: Secrets Vault](architecture/adr/0003-secrets-vault.md)
  - [ADR-004: Persistence (SQL + ImmuDB)](architecture/adr/0004-persistence-sql-immudb.md)
  - [ADR-005: Permission Mapping](architecture/adr/0005-permission-mapping.md)
- [**Diagrams**](architecture/diagrams.md) — System architecture diagrams
- [**Permission Mapping**](permission_mapping.md) — Complete system permission reference

---

## Core Components

### [Gateway](components/gateway/)
Reverse proxy, authentication, token management, role-based access control.

**Key Features:**
- [**Internal Token Management**](components/gateway/features/internal-token/) — JWT token generation & distribution (JWKS)
- [**Role-Based Access Control (RBAC)**](components/gateway/features/role-management/) — Role creation, permission mapping, endpoint access control
- [**API Key Authentication**](components/gateway/features/api-key-auth.md) — API key validation for service-to-service calls
- [**Token Introspection**](components/gateway/features/token-introspection.md) — Token validation and metadata extraction
- [**OIDC Diagnostics**](components/gateway/features/diagnostics/) — OpenID Connect troubleshooting endpoint
- [**Health Checks**](components/gateway/features/health-checks.md) — Service health monitoring

**Configuration:**
- [**Configuration Guide**](components/gateway/Configuration-Guide.md) — Gateway settings and options
- [**Certificate Implementation**](components/gateway/README-Certificate-Implementation.md) — TLS/SSL certificate setup
- [**Simplified Config**](components/gateway/SIMPLIFIED-CONFIG.md) — Quick configuration reference

---

### [Core API](components/core/)
Main business logic, data access, application services, domain models.

**Features:**
- [**Read-Only Mode**](components/core/features/read-only-mode.md) — Feature flag to disable write endpoints
- [**Hot-Path Caching**](components/core/features/caching.md) — Performance optimization for frequently accessed data
- [**Endpoint Access Control**](components/core/features/endpoint-access.md) — Per-endpoint authorization

**Components:**
- [**Services Overview**](components/core/services.md) — User, Project, Provider, Location, Resource management
- [**Database Architecture**](components/core/database.md) — Entity Framework, repositories, migrations

---

### [Infrastructure & Data Access](components/infrastructure-dataaccess/)
Persistence layer, entity configurations, repository implementations, database context.

- [**README**](components/infrastructure-dataaccess/README.md) — Component overview and patterns

---

### [Forge.Core Library](components/forge-core/)
Shared utilities for role management, permission service, import/export, endpoint mapping.

- [**Development Notes**](components/forge-core/README.md) — Architecture and component details

---

## Integrations

### [HEAppE Integration](components/integrations/heappe/)
Command template management, cluster synchronization, resource allocation.

**Complete Guides:**
- [**Command Template Creation — Complete Guide**](components/integrations/heappe/command-templates.md) — Step-by-step walkthrough with examples
- [**Command Template — Quick Reference**](components/integrations/heappe/quick-reference.md) — Cheat sheet for common operations
- [**Template Synchronization**](components/integrations/heappe/synchronization.md) — Sync strategies and background workers
- [**Persistence & Database**](components/integrations/heappe/persistence/README.md) — How template data is stored

---

### [Waldur Integration](components/integrations/waldur/)
Data collection, background synchronization, import/export, provider integration.

**Implementation Guides:**
- [**Data Collection**](components/integrations/waldur/data-collection.md) — REST API client, models, and endpoints
- [**Background Periodic Sync**](components/integrations/waldur/background-periodic-sync.md) — Producer/consumer pattern, processing pipeline
- [**Data Export**](components/integrations/waldur/export.md) — Export UserOrg data back to Waldur
- **Detailed Documentation:**
  - [Implementation Details](components/integrations/waldur/Implementation-Details.md)
  - [Models and Contracts](components/integrations/waldur/Models-and-Contracts.md)
  - [Diagrams](components/integrations/waldur/Diagrams-Details.md)
  - [Data Flow](components/integrations/waldur/Repository-DataFlow.md)
  - [Sequence Diagrams](components/integrations/waldur/Sequence-Userorg-Waldur-HEAppE.md)

---

## Development & Migration

### [Guides](guides/)
Practical how-tos and operational documentation.

- [**Getting Started**](guides/getting-started.md) — Local setup, running the app
- [**Database Operations**](guides/database.md) — Restore, migrations, troubleshooting
- [**Contributing**](guides/contributing.md) — Code style, testing, PR process
- [**Migrating from v1 to v2 endpoints**](guides/v1-to-v2-migration.md) — full route map, contract changes, migration steps
- [**Working with ETags (conditional GET)**](guides/api-conditional-requests-etag.md) — Client integration: `ETag`/`If-None-Match` → `304`

### [DDD Migration](ddd-migration/)
Long-term refactoring project: moving from anemic to rich domain models, API versioning.

**Start here:**
- [**DDD Migration README**](ddd-migration/README.md) — Complete navigation and reading order
- [**Entity Relationship Map**](ddd-migration/entity-relationship-complete-map.md) — All 24 entities and their relationships

**Phases:**
- Phase 1: [Architecture & Pattern Analysis](ddd-migration/ddd-migration-phase1-analysis.md) ✅ Complete
- Phase 2: [DbContext Refactoring](ddd-migration/ddd-migration-phase2-dbcontext.md) 📋 Pending
- Phase 3: [Remove Public ID Properties](ddd-migration/ddd-migration-phase3-entities.md) 📋 Pending
- Phase 6: [API Versioning & Endpoint Refactoring](ddd-migration/ddd-migration-phase6-api-versioning.md)
  - Supplementary: [API Versioning Strategy](ddd-migration/api-versioning-gateway-routing.md)
  - Supplementary: [Model Versioning Evolution](ddd-migration/model-versioning-evolution.md)

---

## API Reference

All endpoints are documented via OpenAPI (Swagger); the committed snapshots live at the repo root
(`API_user.json`, `API_administration.json` for v1, published only while `V1Api` is on;
`API_service.json` for the ungated legacy endpoints; `API_v2-user.json`, `API_v2-provideradmin.json`,
`API_v2-admin.json` for the audience-scoped v2 documents, ADR-0021).

**Moving from v1 to v2?** → [v1 → v2 endpoint migration guide](guides/v1-to-v2-migration.md)

Key endpoint groups:

- **Project Management** — Create, list, update, delete projects; manage project users and resources
- **User Management** — User information, permissions, preferences
- **HEAppE Integration** — Command templates, cluster information, project initialization
- **Waldur Integration** — Synchronization, data collection
- **Administration** — System roles, permissions, configuration

See `src/Core/API/API/` for endpoint implementations.

---

## Search & Troubleshooting

**Looking for something?**
- Team onboarding? → [Architecture Guide](ARCHITECTURE_GUIDE.md)
- How is [component] structured? → Use **Ctrl+F** to find in [Architecture Overview](architecture/index.md)
- API endpoint details? → Check `src/Core/API/API/` directory
- Database schema? → See [Database Architecture](components/core/database.md)
- Permissions not working? → See [Permission Mapping](permission_mapping.md)
- HEAppE sync issues? → Check [Waldur Background Sync](components/integrations/waldur/background-periodic-sync.md)

---

## File Organization

```
docs/
├── index.md                          ← YOU ARE HERE
├── ARCHITECTURE_GUIDE.md             (Team onboarding)
├── REFACTORING_PLAN.md               (Migration roadmap)
├── permission_mapping.md             (Permission reference)
├── architecture/                     (System architecture)
│   ├── index.md
│   ├── adr/                          (Architecture decisions)
│   └── diagrams.md
├── components/                       (Component documentation)
│   ├── gateway/                      (Reverse proxy & auth)
│   │   ├── features/                 (Token, RBAC, OIDC, etc.)
│   │   └── ...
│   ├── core/                         (Business logic)
│   │   ├── features/                 (Read-only mode, caching, etc.)
│   │   └── ...
│   ├── infrastructure-dataaccess/    (Persistence layer)
│   ├── forge-core/                   (Shared libraries)
│   └── integrations/                 (External services)
│       ├── heappe/                   (HEAppE commands & clusters)
│       └── waldur/                   (Data sync & export)
├── guides/                           (How-to documentation, incl. v1→v2 migration)
├── ddd-migration/                    (Long-term refactoring)
└── images/                           (Diagrams & screenshots)
```

---

## Document Status

| Document | Status | Last Updated |
|----------|--------|--------------|
| Architecture Guide | ✅ Current | Active |
| Permission Mapping | ✅ Current | Active |
| HEAppE Command Templates | ✅ Current | 2026-04-24 |
| Gateway Role Management | ✅ Current | Active |
| Waldur Integration | ✅ Current | Active |
| DDD Migration | 📋 In Progress | Phase 6 Ready |
| v1 → v2 Endpoint Migration | ✅ Current | 2026-08-18 |
| Read-Only Mode | ✅ Current | Active |
| Hot-Path Caching | ✅ Current | Active |

---

## Questions?

1. **Architecture questions** → Start with [Architecture Guide](ARCHITECTURE_GUIDE.md)
2. **Implementation questions** → Check component READMEs
3. **Integration questions** → See [HEAppE](components/integrations/heappe/) or [Waldur](components/integrations/waldur/) docs
4. **Missing documentation?** → File an issue with label `documentation`

---

**Last Updated:** 2026-08-18  
**Maintained By:** Dev Team
