# ADR-0026 + ADR-0024 slug — migrations overview

Cleanup outcome: **schema lives in EF migrations** (legacy `Infrastructure.DataAccess`, context
`DatabaseContext`); the only standalone SQL file is the **data fix** that an EF migration cannot express.
Deployment procedure: see **`0026-deployment-runbook.html`**.

## Artifacts

| Artifact | Kind | What |
|---|---|---|
| `20260627143515_AddProjectResourceProviderId` | EF migration | `ProjectResource.ProviderId` nullable column + FK |
| `20260627143547_AddProjectResourceProviderIdNameUniqueIndex` | EF migration | filtered unique `(ProviderId, Name)` (drops the plain FK index) |
| `20260627145647_AddRequestAndAssignedLocationSlug` | EF migration | `Request.Slug` + `AssignedLocation.Slug` + filtered unique indexes (ADR-0024) |
| `sql/0026-projectresource-per-provider-split.sql` | manual data fix | backfill ProviderId, split the 3 cross-provider rows, repoint children, verify-before-commit |

All three EF migrations are additive. The legacy `Infrastructure.DataAccess` owns the schema (ADR-0019
overlay rule); `Core.DataAccess` emits no migrations.

### Deployable SQL set (`docs/ddd-migration/sql/`)

Idempotent per-migration scripts generated via `dotnet ef migrations script`, plus the data fix. This is
the complete manual deployment set (procedure: `0026-deployment-runbook.html`):

| Order | File | Phase |
|---|---|---|
| 1 | `0026-m1-add-projectresource-providerid.sql` | manual bracket — M1 |
| 2 | `0026-projectresource-per-provider-split.sql` | manual bracket — data |
| 3 | `0026-m2-add-projectresource-providerid-name-unique-index.sql` | manual bracket — M2 |
| 4 | `0026-slug-add-request-assignedlocation.sql` | normal path — slug (independent) |

**Deleted in cleanup:**
- `0024-M1/M2/M3-*.sql` (hand-SQL) → M1/M2 folded into the slug EF migration; M3 **superseded by ADR-0026**
  (global `(ServiceName, Name)` uniqueness replaced by per-provider `(ProviderId, Name)`), not migrated.

## How the EF migrations were generated

Model changes first, then scaffold:
- `Domain/Entities/ProjectResource.cs`: `Guid? ProviderId`; config `ProjectResourceTableConfiguration.cs`:
  FK + filtered unique `(ProviderId, Name)`.
- `Domain/Entities/Request.cs` + `AssignedLocation.cs`: `string? Slug`; configs: `nvarchar(120)` +
  filtered unique `Slug` index.

```bash
dotnet ef migrations add AddProjectResourceProviderId               --project src/Core/Infrastructure.DataAccess --context DatabaseContext
# (after adding the unique-index HasIndex to the config)
dotnet ef migrations add AddProjectResourceProviderIdNameUniqueIndex --project src/Core/Infrastructure.DataAccess --context DatabaseContext
dotnet ef migrations add AddRequestAndAssignedLocationSlug           --project src/Core/Infrastructure.DataAccess --context DatabaseContext
```

Each scaffold emitted **only** the intended operations — the legacy snapshot is authoritative, no drift
noise (unlike the Core.DataAccess overlay that the old 0024 hand-SQL headers warned about).

Generate the deployable SQL on demand (offline, no DB connection):
```bash
cd src/Core/Infrastructure.DataAccess
dotnet ef migrations script --idempotent --context DatabaseContext --output ../../../artifacts/full-migration.sql
```

## Ordering — the data fix runs BETWEEN M1 and M2 (manual bracket)

The data fix must sit between migration #1 (ProviderId column) and migration #2 (unique index): backfill
ProviderId after M1, build the unique index only over clean data. So **M1 and M2 are applied manually**,
each from its own `dotnet ef migrations script <from> <to>` output, in the order **M1 → data fix → M2**.
The global CI/CD script is NOT used for this bracket (it would apply M1+M2 back-to-back with no gap).

The **slug migration is independent** (it does not bracket the data fix) and goes the normal way — the
global CI/CD / auto-migration script, after the bracket.

> ⚠️ The app auto-applies migrations on startup (`DatabaseMigrationStartupService` → `MigrateAsync`). That
> would apply M1+M2 together and skip the data fix. Apply the DB changes **before the new app build starts**
> (or disable auto-migrate for the release); the manual scripts write their history rows, so the later
> auto-migrate is a no-op. Full procedure: `0026-deployment-runbook.html`.
>
> (Aside: M2's filter `WHERE IsDeleted = 0 AND ProviderId IS NOT NULL` happens to make the index tolerant
> even if applied early — it builds empty over all-NULL ProviderId — but the deployment still follows the
> strict bracket rather than relying on that.)

## Follow-ups (not schema)

- **Overlay sync:** the modular `Core.Domain/Provider/ProjectResource.cs` + `Core.DataAccess` configs need
  `ProviderId` + the per-provider index to match; confirm a probe `dotnet ef migrations add` on
  `Core.DataAccess` stays empty. (F4 implementation.)
- **v2 resolution change:** `AssignResourceEndpoint` resolves a resource by name globally (ADR-0024 model);
  ADR-0026 moves it to `(providerName, resourceName)`. (F4 implementation.)
