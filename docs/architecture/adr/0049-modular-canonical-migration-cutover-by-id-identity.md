# ADR 0049 - Modular `Core.DataAccess` becomes the canonical EF migration owner by ID-identity adoption (no baseline, no squash)

- Date: 2026-06-30
- Status: accepted
- Relates to: [ADR-0041](0041-outbox-dedicated-schema-isolated-persistence-unit.md) (named this "adopt existing database" work as deferred + risky), [ADR-0028](0028-transactional-outbox-for-domain-events.md). Unblocks the `ApprovalPolicy` column (#230) and `ProjectResourceRequest` table (#232), which now scaffold via the modular context instead of legacy.

## Context

The `dbo` schema has two EF `DbContext`s mapping the same physical tables:

- **Legacy** `UserOrganizationManagement.Infrastructure.DataAccess.Contexts.DatabaseContext`
  (`src/Core/Infrastructure.DataAccess`) — currently **owns the migrations**, runs `MigrateAsync`
  via `DatabaseMigrationStartupService`, and is the dbo context registered in the `DatabaseMigrations`
  tool. Still heavily used at runtime by ~20 legacy repositories, `UserOrgUnitOfWork`, `DataAccessExtensions`,
  `DatabaseSeed`.
- **Modular** `Core.DataAccess.Contexts.DatabaseContext` (`src/Modules/Core.DataAccess`) — the refactor
  target. Maps the same dbo tables via `Core.Domain.*` aggregates + value-object conversions + shadow
  properties. Today it **emits no migrations** and never runs `MigrateAsync` (it is a read/write overlay).

The intent (historically called ADR-0047 but never recorded) is for the modular context to become the
**canonical** migration owner. ADR-0041 deferred this as the risky "adopt existing database" task:
regenerate a drifted snapshot, give the context its own history, and **baseline** every legacy table as
already-applied.

**Re-investigation (2026-06-30) shows the feared blast radius is largely gone:**

- The modular `Migrations/` folder is **a copy of the legacy migrations** — same migration **IDs**, same
  namespace, and it shares the **default `dbo.__EFMigrationsHistory`** with legacy.
- The modular entity configs already reproduce the real schema per-table (Country, Location, etc. snapshot
  output matches legacy). The "111 ALTER / 14 DROP" probe result recorded earlier is **stale** — it predated
  the config refactor.
- The only real drift: the modular project is **behind by 5 migrations** (the Jun 27–30 work:
  `AddProjectResourceProviderId`, `AddProjectResourceProviderIdNameUniqueIndex`,
  `AddRequestAndAssignedLocationSlug`, `AddProviderNameUniqueIndex`, `AddOnboardingRequestTable`),
  so its `ModelSnapshot` is stale by exactly those.

Because the modular migration files carry the **same IDs already present in every live DB's
`__EFMigrationsHistory`**, an existing prod/staging database sees them as **already applied** — there is
**no baseline insert and no history reconcile to perform.** This is adoption by *ID-identity*, not by
*baseline*.

## Decision

The modular `Core.DataAccess.Contexts.DatabaseContext` becomes the **canonical owner** of `dbo` migrations,
adopted by **ID-identity**:

1. **Sync modular to the current schema.** Bring the 5 missing migrations into the modular project
   (verbatim, IDs preserved) and **regenerate the modular `DatabaseContextModelSnapshot`** so it reflects
   the real schema. **Keep all migration files — no squash, no baseline.** Rename the migration namespace
   to `Core.DataAccess.Migrations` (cosmetic; the `[Migration("<id>")]` attribute, not the namespace,
   defines the history ID, so live history is untouched).
2. **Hard acceptance gate — empirical zero-diff.** After the sync,
   `dotnet ef migrations add _Probe -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess` must
   scaffold an **empty `Up()`/`Down()` across all dbo tables**. The probe is reverted. A non-empty probe
   means the modular config still diverges from the real schema; that divergence is fixed (scope = **all**
   dbo tables, including the half-scaffolded Provider/Configuration/Request/Location aggregates) until the
   probe is empty. **No DDL may ever be emitted against the live schema.**
3. **Flip ownership in the tool + runtime.** In `DatabaseMigrations`, repoint the dbo context in both
   hardcoded lists (`GetAllDbContextTypes()` and the `GenerateScriptAsync` `contextProjects` map) from the
   legacy context/`Infrastructure.DataAccess.csproj` to the modular context/`Core.DataAccess.csproj`. Add a
   modular `DatabaseMigrationStartupService` (same fail-fast, `Database:AutoMigrateOnStartup`-gated pattern)
   that runs `MigrateAsync` on the modular context, and **remove the legacy startup migrator**.
4. **Regression gate.** `DatabaseMigrations script` must produce a **byte-identical** `migration.sql`
   before and after the flip (same IDs → same SQL), and `check` against a current-history DB must report
   **0 pending**.
5. **Retire legacy migration ownership only.** Delete `src/Core/Infrastructure.DataAccess/Migrations/`
   (the duplicate copy + legacy snapshot), drop the `Infrastructure.DataAccess` `ProjectReference` from
   `DatabaseMigrations.csproj`. **Keep the legacy `DatabaseContext` class and its repositories** — they
   remain the runtime overlay for un-migrated legacy features. This inverts the current overlay roles:
   modular owns + applies migrations; legacy becomes the migration-less runtime overlay.

## Consequences

- **Zero-risk on live databases.** No `__EFMigrationsHistory` rows are added, deleted, or rewritten; no
  DROP/ALTER is generated. The `MakeSqlIdempotent` tool (which guards only CREATE) is therefore never
  asked to protect a destructive statement during cutover.
- **The canonical path is immediately exercised** by #230 (column) and #232 (table): they become the first
  real `dotnet ef migrations add` against the modular context — the proof the cutover works.
- **Two `DatabaseContext` types share the simple name** during and after transition; tool code must
  fully-qualify or alias to avoid `typeof(DatabaseContext)` ambiguity.
- **The bulk of the effort and the only real uncertainty is step 2** — driving the probe to empty across
  the half-scaffolded aggregates. If a config cannot match the real schema without an actual schema change,
  that is a **clarification stop**, never a silently-emitted migration.
- **Legacy context lives on.** This ADR retires legacy *migration ownership*, not the legacy context. Its
  full removal is the separate, larger feature-migration effort and is **out of scope**.
- **First runtime apply is the verification point** (feature-level Aspire): `db-migrations-apply` runs the
  modular migrations and the API's modular startup migrator reports 0 pending against a current DB.

## Considered options

- **Baseline / adopt-existing (ADR-0041's feared path) — rejected as unnecessary.** Regenerating a
  from-scratch `Init` + baselining every dbo table would force a history reconcile on live DBs. The
  ID-identity of the already-copied migration files makes this entirely avoidable.
- **Squash 29 migrations into one baseline — rejected.** Cleaner history, but requires deleting 29 rows
  and inserting 1 on every live DB (ops runbook + prod risk), contradicting the "legacy migrations stay
  valid in place" goal.
- **Keep all files, adopt by ID-identity (chosen).** Smallest, non-destructive change; live history stays
  valid as-is; the only new work is a snapshot regen + driving the probe to zero-diff + flipping the tool.
