# Runbook #242 — RBAC seed changes that need a manual import in deployed environments

The Gateway now reconciles the RBAC seed **additively** on every startup (ADR-0060): entries *added*
to `roleManagmentRBAC.json` reach every environment automatically on deploy. **Changed** and
**removed** entries do NOT propagate — they require this one-off manual step per environment.

## What #242 changed that does NOT propagate automatically

| Change | Seed state | Deployed-DB state until manual step |
|---|---|---|
| `POST /api/v2/Providers/{providerName}/Reactivate` remapped `perm:provider:remove` → `perm:provider:update` | updated | still `perm:provider:remove` (ProviderAdmin keeps getting 403) |
| Stale `GET /api/v2/locations/{id}` mapping removed | removed | still present (harmless — route no longer exists) |

Everything else in #242 (nested location GET, `ResourceRequests/{requestId}`,
`enums/allocation-unit-types`) is **additive** and lands automatically via startup reconciliation.

## Manual step (per environment, once, after deploying this release)

1. Open the RoleManagement admin UI (Forge) → Import/Export.
2. Import the release's `roleManagmentRBAC.json` with **`overwriteExisting=true`**.
   - This updates the reactivate mapping in place and re-syncs mappings to the seed.
   - Runtime-added custom mappings not present in the seed are left alone by the import,
     but any mapping whose name exists in the seed is overwritten — review UI-made
     adjustments to seed-named entries before importing.
3. Optionally delete the stale `GET /api/v2/locations/{id}` mapping via the UI (cosmetic).
4. No Gateway restart needed — the import invalidates the endpoint-permission snapshot cache.

## T-196: `enums/allocation-unit-types` moved to `allocation-unit-types`

T-196 removes the seed entry `GET /api/v2/enums/allocation-unit-types` (the route no longer exists) and adds
`GET /api/v2/AllocationUnitTypes` and `GET /api/v2/AuthenticationTypes`, both `permissionKeys: []`. The two
additions land automatically; the removal does not.

1. Import the release's `roleManagmentRBAC.json` with **`overwriteExisting=true`**, as in the manual step above.
2. Delete the stale `GET /api/v2/enums/allocation-unit-types` mapping via the UI. It is harmless while it stays
   (the route answers 404), so this is cleanup, not a fix.
3. Verify: `GET /api/v2/AllocationUnitTypes` and `GET /api/v2/AuthenticationTypes` as any authenticated
   user -> 200, not 403.

## Verification

- `POST /api/v2/Providers/{name}/Reactivate` as a ProviderAdmin of that provider → no longer 403 at the Gateway.
- `GET /api/v2/ResourceRequests/{requestId}` as ProviderAdmin/Admin/ProjectOwner/ProjectManager → 200/404, not 403.
- `GET /api/v2/Providers/{name}/Locations/{location}` → 200 (was 403).
- Anonymous `GET /api/v2/Public/Requests/ByLink/{link}` → 200 (was 401).

Standing rule going forward (ADR-0060): ship new endpoints with their seed mapping in the same
release (auto-propagates); any *change* or *removal* of an existing seed entry carries a manual
import step in that release's runbook.
