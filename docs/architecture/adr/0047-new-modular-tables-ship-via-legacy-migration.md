# ADR 0047 - OnboardingRequest table ships via a legacy Infrastructure.DataAccess migration (modular-canonical cutover deferred)

- Date: 2026-06-30
- Status: accepted
- Relates: ADR-0004 (persistence), ADR-0041 (Outbox isolated history), ADR-0046 (the table)
- Issue: [work item #228](https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/228)
- Memory: `project_migration_tool_dual_context_lists`, `project_modular_migrations_overlay`
- Supersedes the earlier draft of this ADR (which planned to author the migration in modular
  `Core.DataAccess` and wire that context into the tool) — reversed during implementation on
  concrete evidence (see Context).

## Decision

For issue #228, the `OnboardingRequest` table's EF migration is authored in **legacy
`src/Core/Infrastructure.DataAccess/Migrations/`** — the context the `DatabaseMigrations` tool and
CI already apply — mirroring the `AddUserProjectAccessTable` precedent. The migration creates the
physical table only; the **modular `Core.DataAccess` EF config (B02) maps to that table at runtime**
for all reads/writes. The two must agree on column names/types/indexes/FKs (the modular config is the
source of truth for the shape; the legacy migration must reproduce it exactly).

The intended end-state — modular `Core.DataAccess` as the canonical migration home — is **deferred to
a follow-up issue**, because it requires prerequisite work that is out of scope here (see below).

## Context

- The earlier plan (modular context authors the migration + is wired into the tool with its own
  isolated `__EFMigrationsHistory`, relying on the tool's idempotent `CREATE` wrap) was found
  **unsafe during B02**: the modular `DatabaseContextModelSnapshot` is **stale** — it still describes
  77 `UserOrganizationManagement.Domain.Entities.*` types and **zero** `Core.Domain.*` types, i.e. it
  predates the value-object/namespace refactor. Applying the modular context with a fresh history
  table therefore replays its **entire** accumulated history — **14 `DROP TABLE` + 111 `ALTER TABLE`
  + 47 `ALTER COLUMN`** + renames — against the live shared database. The tool's
  `MakeSqlIdempotent` only guards `CREATE TABLE`/`CREATE INDEX`; the DROP/ALTER are unguarded and
  destructive on first apply.
- So the modular-canonical path cannot ship a new table safely until the modular snapshot is rebuilt
  to the live model — a separate, sizeable change. The legacy context, by contrast, has a current
  snapshot and is already the applied one (tool + CI + `DatabaseMigrationStartupService`).
- This is exactly the "first time the overlay context is applied to a real DB → raise a
  clarification" risk the earlier draft flagged. The developer's call (2026-06-30): ship #228 via
  legacy now; do the modular cutover as a follow-up.

## Considered options

- **Legacy migration now + follow-up for the modular cutover (chosen).** Zero-risk: rides the
  already-applied legacy context with a current snapshot; ships through tool + CI unchanged. Cost: a
  v2-native table's migration lives in the legacy tree until the cutover.
- **Modular migration + isolated history + idempotent wrap.** Rejected: unsafe today — the stale
  modular snapshot makes the overlay's history non-idempotent (DROP/ALTER replayed live).
- **Baseline the modular history per environment.** Rejected for #228: needs a per-env seeding step
  and still leaves the snapshot stale (future `migrations add` keeps emitting spurious diffs).
- **Rebuild the modular snapshot now.** Rejected for #228: large, separate change that balloons
  scope; it is the follow-up.

## Consequences

- The migration lives in `src/Core/Infrastructure.DataAccess/Migrations/` and is applied by the
  existing tool + CI with no tool-list changes. The modular `Core.DataAccess.DatabaseContext` is NOT
  wired into the tool (the earlier draft's wiring is not done).
- **Acceptance:** the generated legacy migration creates ONLY the `OnboardingRequest` table + its
  indexes/FKs (nothing else); legacy `DatabaseContextModelSnapshot` updates accordingly;
  `DatabaseMigrations check`/`script` stays green; the table shape matches B02's
  `OnboardingRequestConfiguration` exactly (Id uniqueidentifier PK with NO default — app assigns
  Guid v7; ProjectId/UserId FK→Project(Id)/User(Id) NoAction; ContactEmail nvarchar(256);
  State int; Slug nvarchar(120) filtered-unique; IsDeleted bit; CreatedAt/ModifiedAt).
- **Follow-up issue:** rebuild the modular `Core.DataAccess` snapshot to the live VO/namespace model,
  then move the migration home to modular and wire the context into the `DatabaseMigrations` tool
  (the deferred modular-canonical cutover). Until then, new modular tables ship via legacy.
