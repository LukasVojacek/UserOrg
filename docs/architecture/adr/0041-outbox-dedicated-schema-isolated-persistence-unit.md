# ADR 0041 - The transactional outbox is a dedicated, schema-isolated persistence unit (HEAppE-style)

- Date: 2026-06-29
- Status: accepted
- Relates to: [ADR-0028](0028-transactional-outbox-for-domain-events.md) (the outbox itself), [ADR-0040](0040-outbox-dispatcher-delivers-sync-to-completion.md) (delivery), [ADR-0019](0019-self-onboarding-accept-single-transaction.md) (overlay: legacy owns the `dbo` schema).

## Context

ADR-0028 introduced an `Outbox` table written in the same transaction as the aggregate change. The
open question was *where* that table's schema + migration live, given the modular `Core.DataAccess`
`DatabaseContext` is an **overlay** on a legacy-owned `dbo` schema: it maps the same tables legacy
created, shares `dbo.__EFMigrationsHistory` with legacy, and its EF model snapshot is frozen/drifted
at legacy CLR names (F4 overlay debt). The runtime applies **only** legacy `Infrastructure.DataAccess`
migrations; `Core.DataAccess` emits none.

Two shapes were considered (and a third, see below):

- Make `Core.DataAccess` own + apply its migrations for everything (the dbo tables too). This needs
  the deferred, risky "adopt existing database" work: regenerate the drifted snapshot, give the context
  its own migration history, and **baseline** every legacy-owned table as already-applied so a runtime
  `MigrateAsync` doesn't try to recreate them. High blast radius on a shared schema.
- A **dedicated, schema-isolated outbox persistence unit** modelled on the **HEAppE integration**
  (`HeappeIntegrationDbContext`): its own context, its own DB schema, its own `__EFMigrationsHistory`
  (default name) **in that schema**, its own startup migrator, migrations scaffolded by `dotnet ef`
  from a clean `Init`.

## Decision

The outbox is a **dedicated persistence unit**, isolated by **its own DB schema** (`outbox`), exactly
like the HEAppE integration:

- A new **`OutboxDbContext`** maps a single `OutboxMessage` entity to `outbox.Outbox`. Default schema =
  `outbox`; migrations-history = the **default-named** `__EFMigrationsHistory` **in the `outbox`
  schema** (set via `HasDefaultSchema("outbox")` + `MigrationsHistoryTable("__EFMigrationsHistory",
  "outbox")` on both the runtime registration and the `IDesignTimeDbContextFactory`).
- Migrations are **scaffolded with `dotnet ef`** from a clean `Init` (no hand-written migrations). Because
  the `outbox` schema is new and empty, the migration only ever creates the outbox table — it never
  touches `dbo`, the legacy snapshot, or legacy's migration history. **No baseline / adopt-existing
  work is required.**
- A **`OutboxMigrationStartupService`** (clone of `HeappeMigrationStartupService`) applies the outbox
  migrations at runtime, gated by `Database:AutoMigrateOnStartup`, with the same fail-fast
  pending-migration check.
- **Atomicity (ADR-0028) across two contexts:** the outbox row must commit in the same transaction as
  the aggregate change. Since the aggregate is saved by the modular `DatabaseContext` (the UnitOfWork)
  and the outbox row is written by `OutboxDbContext`, the two **share one DB connection and one
  transaction** (EF `DbContext.Database.UseTransaction`) — the standard cross-context same-transaction
  pattern. This is implemented in the UnitOfWork write path (block O2), not here.

## Consequences

- **No risky schema migration.** The outbox unit is additive and self-contained; the legacy-owned `dbo`
  schema, the drifted modular snapshot, and `dbo.__EFMigrationsHistory` are all untouched. The big
  "Core.DataAccess owns + baselines its migrations" task is **deferred** to when modular-owned tables
  in `dbo` actually need it — it is explicitly **not** a prerequisite for the outbox.
- **`dotnet ef` works cleanly** for the outbox context: its snapshot is born correct (clean `Init`), so
  future outbox schema changes scaffold as normal diffs.
- **Same physical DB, isolated schema.** `outbox.__EFMigrationsHistory` cannot collide with legacy's
  `dbo.__EFMigrationsHistory` or HEAppE's `heappe_integration.__EFMigrationsHistory`.
- **Cross-context transaction is the cost.** The UnitOfWork must enlist the `OutboxDbContext` write in
  its transaction (O2). This is well-supported but is real plumbing — the price of schema isolation
  over keeping the outbox in the main context.
- **First runtime apply is the verification point.** `MigrateAsync` against a real DB (needs
  `USERORG_TEST_DB`) confirms the outbox schema + table are created; this is verified by the developer/CI,
  not in the implementation worktree.

## Considered options

- **Outbox inside `Core.DataAccess` with the context owning its migrations (rejected for now).** Simplest
  atomic write (one context), but forces the deferred adopt-existing-database baseline on a shared,
  legacy-owned schema — high risk for no immediate benefit.
- **Hand-written legacy `Infrastructure.DataAccess` migration (rejected).** Would create the table at
  runtime, but the team standard is `dotnet ef`-scaffolded migrations (no hand-writing), and it keeps the
  outbox coupled to the legacy schema's lifecycle.
- **Dedicated schema-isolated `OutboxDbContext` (chosen).** Clean `dotnet ef` migrations, zero blast
  radius on the legacy schema, mirrors the proven HEAppE pattern; accepts cross-context transaction
  plumbing for atomicity.
