# HEAppE Integration

Integration with High-End Application Platform (HEAppE) for command template management, cluster information, and resource allocation.

## Overview

The HEAppE integration enables:
- **Command template management** — Create, activate, deactivate, synchronize templates
- **Cluster discovery** — List partitions, retrieve cluster information
- **Resource allocation** — Check user allocations, validate cluster access
- **Project initialization** — Set up projects in HEAppE clusters
- **Synchronization** — Sync project resources with HEAppE

**Location:** `src/Modules/Integrations/Heappe/`

## Key Components

### Command Template Management
- `CommandTemplateManagementService` — Core CRUD operations
- `CreateTemplateEndpoint` — Create new templates (POST)
- `GetCommandTemplatesEndpoint` — List templates (GET)
- `ActivateCommandTemplatesEndpoint` — Activate templates (PUT)
- `DeactivateCommandTemplatesEndpoint` — Deactivate templates (PUT)
- `CommandTemplateMigrationEndpoint` — Migrate existing templates

**See:** [Command Template Creation Guide](command-templates.md) and [Quick Reference](quick-reference.md)

### Template Permissions
- `CommandTemplatePermissionsEndpoint` — Manage template access
- `CommandTemplatePermissionQueryHandler` — Query user permissions
- `UserCommandTemplatePermissionModel` — Data model

### Template Synchronization
- `ProjectCommandTemplateSyncService` — Sync with HEAppE cluster
- `ProjectCommandTemplatesSyncEndpoint` — Sync endpoint (PUT)
- Background workers for async synchronization

### Cluster Information
- `GetPartitionsEndpoint` — List cluster partitions
- `ClusterInfoCacheWarmer` — Cache cluster data for performance
- Caching strategy for partition information

### Connection Management
- `IHeappeConnectionFactory` — The one door to HEAppE: public, service and user connections for a `HeappeIdentifier`
- `HeappeConnectionService` — Manage HEAppE cluster connections
- `HeappeCredentialsService` — Store and retrieve credentials
- `HeappeVersionResolver` — Detect HEAppE version

Every HEAppE call is sent to the NexusApi proxy, `{TokenExchange:Nexus:BaseUrl}/api/heappe/{identifier}/`, never to a
URL stored with the location ([ADR 0117](../../../architecture/adr/0117-heappe-calls-go-through-the-nexusapi-proxy-by-identifier.md),
[ADR 0119](../../../architecture/adr/0119-nexusapi-settings-under-token-exchange-and-a-startup-gate.md)).
The identifier is the `HEAPPE_IDENTIFIER` location value (`^[a-z0-9][a-z0-9-]{0,63}$`); `HEAPPE_URL` is kept for logs.
`Features/Nexus/` holds the registry client, the 60 s instance directory and the service-account token handling.

`FeatureManagement:TokenExchangeEnabled` is read once at startup and decides what is registered; a change needs a
restart. With the flag on the registry resolves each identifier and user calls carry a token exchanged per instance
([ADR 0118](../../../architecture/adr/0118-caller-token-is-exchanged-per-heappe-instance.md)). With it off (or absent)
no registry, NexusApi service account or TokenDelegation is registered: every identifier goes to the proxy and user
calls carry the caller's token unchanged; NexusApi answers 404 for an unknown or disabled instance. One Information
line at startup names the mode (`HEAppE routing mode: token exchange` or `HEAppE routing mode: pass-through`).

**Deployment.** The Core API needs
- `TokenExchange__Nexus__BaseUrl`, always: the NexusApi address, an absolute http or https URI at the host root,
  without path, userinfo, query or fragment. Use https unless the network segment is not shared (risk R-03). The API
  does not start without it;
- `FeatureManagement__TokenExchangeEnabled` (default `false`), `true` or `false` only; the kill switch of the
  per-instance token exchange. Any other value, or the `EnabledFor` form, fails startup;
- with the flag on, `TokenExchange__Nexus__ClientId` and `TokenExchange__Nexus__ClientSecret`, the NexusApi service
  account with `registry:read`. The API does not start without them;
- with the flag on, `TokenExchange__ClientId` and `TokenExchange__ClientSecret`, the client that performs the
  exchange against `TokenExchange__Authority`. Without one of them the API starts with a Warning and a user call
  fails with `token-exchange-failed`;
- optionally `TokenExchange__Nexus__DirectoryCacheTtl` (default `00:01:00`, between `00:00:01` and `01:00:00`).

Startup failures name the key, never the value.

**Upgrade from the old keys.** The old keys are ignored; set the new ones before deploying:

| old | new |
|---|---|
| `services__nexusapi__http__0`, `services__nexusapi__https__0`, `services__nexusapi__default__0` | `TokenExchange__Nexus__BaseUrl` |
| `Nexus__ClientId` | `TokenExchange__Nexus__ClientId` |
| `Nexus__ClientSecret` | `TokenExchange__Nexus__ClientSecret` |
| `Nexus__DirectoryCacheTtl` | `TokenExchange__Nexus__DirectoryCacheTtl` |

Rolling back to an older image needs the old keys back; the flag alone is not a rollback across this release.

**Local.** `aspire run` asks in the dashboard which NexusApi to use, like the database source and the identity
provider; `--Parameters:NexusApiSource=Container|External` answers it up front. `Container` starts NexusApi as a
container (parameters `NexusApiImage`, `NexusApiImageTag`). The default is
the pinned `opencode.it4i.eu:5050/lexis-platform/backend/nexusapi/api:0.0.0-rc.310`; NexusApi CI never pushes `latest`,
so bump `NexusApiImageTag` to use a newer NexusApi. For a locally published image use
`--Parameters:NexusApiImage=nexusapi-api --Parameters:NexusApiImageTag=local`.
`External` uses an existing NexusApi at `NexusApiBaseUrl`. With `FeatureTokenExchangeEnabled` on, it signs in as
`NexusApiServiceAccountClient` (default `userorg`) with `NexusApiServiceAccountSecret`. The container's registry
routes the `heappe-mock` instance to the HEAppE WireMock (`src/UserOrg.AppHost/heappe-mappings/`).

### Project Initialization
- `ProjectCreationService` — Initialize projects in HEAppE
- Cluster selection and setup

### Background Synchronization
- `BackgroundSync/` directory — Async sync workers
- `ChannelWorker` — Channel-based processing pattern
- `WorkItemProcessor` — Process sync work items
- `InProgressTracker` — Track ongoing operations
- `HeappeSyncTelemetry` — Observability

### Database Persistence
- `HeappeDbInitializerService` — Initialize persistence
- `HeappeIntegrationDbContext` — Entity Framework context
- Entity configurations for templates, permissions, credentials

## Complete Guides

### [Command Template Creation Guide](command-templates.md)
Step-by-step walkthrough with examples and validation logic.

**Covers:**
- Endpoint: `POST /api/Heappe/CommandTemplates`
- Request body format
- 3-phase creation process (validation, preparation, creation)
- HEAppE API interactions
- Error handling

### [Quick Reference](quick-reference.md)
Cheat sheet for common operations.

**Quick answers for:**
- Required fields
- API endpoint format
- Typical request/response examples
- Troubleshooting

### [Synchronization](synchronization.md)
How templates are synchronized with HEAppE clusters.

**Topics:**
- Sync strategies
- Background workers
- Caching behavior
- Conflict resolution

### [Persistence & Database](persistence/README.md)
How template data is stored and managed.

## Diagrams

Visual representations of key flows:
- `flow_create_template.excalidraw` — Complete template creation flow
- `flow_create_template_simple.excalidraw` — Simplified flow for reference

## Typical Template Creation Flow

1. **Validation Phase** — Check project, resource, cluster exist
2. **Preparation Phase** — Prepare request for HEAppE API
3. **Creation Phase** — Send to HEAppE, store in database
4. **Synchronization** — Sync permissions and metadata

## Integration with Other Components

- **Core API** — User/project/resource context
- **Waldur** — Export template information
- **Gateway** — Authentication and role-based access
- **Infrastructure** — Database persistence

## Status

✅ Fully implemented and production-ready

- Complete CRUD operations
- Template synchronization
- Background workers for async operations
- Comprehensive persistence layer
- Version detection and adaptation
