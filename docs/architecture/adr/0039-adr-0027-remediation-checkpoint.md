# ADR 0039 - ADR-0027 uniqueness remediation checkpoint: #3 closed, #1/#2 deferred with explicit lift-conditions, slug staged

- Date: 2026-06-28
- Status: accepted (2026-06-28). Records the disposition of the three ADR-0027 uniqueness gaps as of this date and the concrete conditions under which the two deferred items become actionable. Does **not** supersede [ADR-0027](0027-v2-natural-key-uniqueness-gaps.md) — that remains the tracking record.
- Relates to: ADR-0027 (the three gaps), ADR-0024 (Request/AssignedLocation slug natural key), ADR-0026 (ProjectResource per-provider migration bracket + runbook)

## Context

ADR-0027 records three natural-key uniqueness gaps, each needing **(a)** an aggregate/use-case guard with
tests and **(b)** an additive filtered-unique DB index preceded by a one-time de-dup. A planning review on
2026-06-28 confirmed all three **guards are implemented and tested**. The open question was how far to push
the DB-index half, which ADR-0027 itself gates on "legacy-writer retirement (F1/F3)". The developer's call
(2026-06-28): **defer #1/#2 entirely, verify only; close #3; stage the ADR-0024 slug migration as a runbook
step.**

## Decision

| Gap | Guard / index state | Disposition |
|---|---|---|
| #1 `ProjectTag.Name` | Guard DONE (resolve-or-create use-case + idempotent `Project.AssignExistingTag`, tested); index **deferred** | **Deferred** — index + de-dup wait for the legacy tag-create writer to retire |
| #2 `UserPermission (UserId, ProjectId)` | Guard DONE (`Project.UpsertMemberRole`, tested); index **deferred** | **Deferred** — index + de-dup wait for the legacy role-write writer to retire |
| #3 `Provider.Name` | Guard DONE (`ProviderExistsAsync` 409); index DONE (`20260628081747_AddProviderNameUniqueIndex`, filtered `[IsDeleted] = 0`); no de-dup needed | **Closed** |

### Lift-conditions for the deferred indexes (#1, #2)

The blocker is not capacity — it is that the **legacy v1 writers are still mapped and still mint
duplicates**, so an index added now fails to build over existing violators or breaks legacy at runtime:

- #1 — `POST /api/ProjectTag` (`src/Core/API/API/ProjectTagEndpoint.cs`)
- #2 — `PUT /api/Project/{ProjectShortName}/User/{IdpUserId}` and `.../Assign/{RoleId}` (`src/Core/API/API/ProjectEndpoint.cs`)

**Each index + its one-time de-dup is applied only after the matching legacy writer above is retired**
(F1 tag-create decommission / F3 role-write decommission). Until then the in-aggregate/use-case guard is the
sole enforcement, which is correct because all v2 traffic flows through it; the index was only ever a
concurrency backstop (ADR-0027 "Enforcement is aggregate-first").

### Slug migration is staged, not applied

`AddRequestAndAssignedLocationSlug` (ADR-0024; additive nullable `Slug` columns + filtered-unique indexes) is
**migration #3 of a 4-migration pending chain**. The app auto-applies the whole pending set on startup
(`DatabaseMigrationStartupService.MigrateAsync`), but chain entries #1/#2 (the ADR-0026 ProjectResource
bracket) require a **manual data-fix bracket** — backfill `ProviderId` *between* M1 and M2. A naive
auto-migrate runs M1+M2 back-to-back and skips the data fix, building the unique index over un-backfilled
data. Therefore the slug column is applied through the **controlled deployment runbook**
(`docs/ddd-migration/0026-deployment-runbook.html`; SQL `docs/ddd-migration/sql/0026-slug-add-request-assignedlocation.sql`)
in order **M1 → data-fix → M2 → slug**, not by an agent and not by an unguarded app startup.

## Consequences

- **No production code or test change** results from this checkpoint — the guards already exist; this ADR
  plus a `progress.md` tracker update is the deliverable.
- **No migration is applied to any database** by the harness. Slug application remains a deployment action
  the developer/CD runs via the existing runbook.
- The deferred work is now expressed as **named, falsifiable lift-conditions** (the two legacy endpoints)
  rather than the looser "gated by F1/F3", so the eventual implementer knows exactly what unblocks each index.
- ADR-0027 is unchanged and remains the authoritative tracking record; this ADR is its dated disposition.
