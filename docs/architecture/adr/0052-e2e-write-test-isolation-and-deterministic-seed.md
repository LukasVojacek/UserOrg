# ADR-0052 — E2E write-test isolation and deterministic seed strategy

- **Status:** Proposed
- **Date:** 2026-07-02
- **Context:** v2 Gateway endpoint integration-test coverage
- **Supersedes:** —

## Context

The 43 uncovered v2 endpoints are mostly **mutations**, run against a single
Aspire-spun SQL Server shared across the whole test run. Naively, shared mutable
state makes write-tests order-dependent and flaky. We also need a realistic seed:
the endpoints read composite fixtures (ProjectResource + AssignedLocation, Request
rows, Provider+Location) that a `user + project` baseline does not provide.

## Decision

**Seed:** keep a **curated, deterministic `seed-data.sql`**. Its shape is derived
from a **one-time** restore of a real production-like backup (via the existing
`WithDatabaseRestoreCommand()`), inspected to learn what real rows look like, then
**discarded**. The test substrate is never the live backup — that would be
non-deterministic and carry PII.

**Isolation (default):** each write-test creates its own **uniquely-named entities**
(GUID-suffixed short-names) and asserts only on those. No teardown — the DB is
ephemeral per run. This is parallel-safe and matches the fixture's existing
dynamic-bootstrap pattern.

**Reset (escape hatch):** provide a "reset DB to seed-only" custom command on the
SQL Server resource, implemented via a **SQL Server database snapshot** taken
post-seed and reverted with `RESTORE ... FROM DATABASE_SNAPSHOT`. Exposed as a
fixture helper `ResetToSeedAsync()`. Used **only** by tests that genuinely need a
pristine global state (statistics, global counts, `ResetAllCaches`) — not per-test,
because a revert serializes the suite.

## Consequences

- **+** Fast suite: most write-tests run in parallel with no reset.
- **+** Deterministic assertions (exact counts/IDs) against the curated seed.
- **+** Snapshot revert is near-instant and sidesteps FK-ordered truncation +
  identity reseed.
- **−** Unique-naming discipline required in every write-test (a collision surfaces
  as a flaky failure).
- **−** Snapshot support depends on the SQL Server container image;
  `ponytail:` fallback is FK-ordered truncate + re-run `seed-data.sql` for the few
  reset-needing tests.

## Alternatives rejected

- **Reset before every mutation test** — serializes the whole write-suite; too slow.
- **Respawn/migrate a fresh DB per test class** — heavy (restore/migrate cost),
  fights the single-AppHost fixture model.
- **Static seed + delta asserts only** — order-dependent and brittle as the seed
  grows.
- **Live backup as the test DB** — non-deterministic, PII, breaks on snapshot change.
