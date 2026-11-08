# Additive RBAC seed reconciliation at Gateway startup

## Context

`RbacSeedHostedService` imports `roleManagmentRBAC.json` only when the RBAC store is **empty**
(`GetRolesAsync().Value.Count > 0` → skip; import always runs with `overwriteExisting: false`).
There is no startup reconciliation: once any roles exist, new entries added to the seed JSON are never
applied on restart. Every seed change therefore requires a manual re-import per environment — the exact
operational gap that let the #242 mapping defects (ADR-0059) go unnoticed in deployed environments, and
the reason parity fixes needed "reimport + Gateway restart" side-channels.

A full reconciliation (upsert changed entries, delete absent ones) was considered and rejected for now:
it would silently overwrite runtime adjustments admins make through the RoleManagement UI, and
delete-semantics turn the seed into a single source of truth the UI then fights against. A versioned
apply-once seed migration was rejected as disproportionate scope for this fix.

## Decision

`RbacSeedHostedService` performs **additive reconciliation** on every startup instead of the
empty-DB-only import:

- **Missing permissions** (by permission key), **missing endpoint mappings** (by **endpoint name** —
  the identity the Forge.Core importer matches on; note this is *not* path template + HTTP method, so a
  re-pathed mapping kept under an existing name is treated as existing and skipped), and **missing
  roles** (by role name) present in the seed but absent in the store are **added**.
- **Existing entries are never modified or deleted.** Changed mappings, changed role→permission
  assignments, and entries removed from the seed do **not** propagate — they remain a deliberate manual
  step via the RoleManagement UI import with `overwriteExisting=true`, documented in the release runbook.
- The empty-store case degenerates to the current full import (everything is "missing").
- After any insertion the endpoint-permission snapshot cache (`EndpointPermissionService:Snapshot`) is
  invalidated so the running Gateway picks the additions up without a rebuild.

## Consequences

- Forward-only seed additions (the common case: a new endpoint ships with its mapping) reach every
  environment automatically on deploy; unmapped-route 403s of the ADR-0059 kind cannot recur for *new*
  routes whose mapping ships in the same release.
- Renames are additions: a mapping under a **new endpoint name** is inserted and any old-name mapping
  stays in place (stale but harmless for removed routes, since the route no longer exists); changing the
  path or method **under an existing endpoint name** does not propagate at all — both cases stay manual.
- Runtime UI adjustments are never clobbered by a restart.
- Ops runbook gains one standing rule: "changed or removed seed entries require a manual UI import with
  overwrite," and the #242 release notes carry the two concrete manual items (reactivate remap, stale
  flat-location route removal).
