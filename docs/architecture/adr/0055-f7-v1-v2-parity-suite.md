# ADR-0055: F7 — v1↔v2 behavioral parity test suite (live side-by-side, reads-first)

- **Status:** Proposed — §D2 superseded by [ADR-0069](0069-testcontainers-only-e2e-retire-aspire-boot.md)
- **Date:** 2026-07-08
- **Deciders:** developer + planning session `20260708T141806Z-f7-parity-suite`
- **Related:** [ADR-0018](0018-read-scope-tiers.md) (read-scope tiers),
  [ADR-0022](0022-provider-nested-resource-routes.md) (nested resource routes),
  [ADR-0051](0051-e2e-external-service-faking-wiremock.md),
  [ADR-0052](0052-e2e-write-test-isolation-and-deterministic-seed.md);
  issue #238; `docs/ddd-migration/{next-phase-plan,v1-retirement-parity,progress}.md`

## Context

The Core→v2 DDD refactor (branch `feat/refactor-core`) is functionally complete
(F0–F6). The **last cutoff gate is F7**: an automated proof that the v2 stack is
behaviourally equivalent to the legacy v1 stack, so `src/Core` can be deleted.

Today there is **no cross-stack test**. `Tests/E2E/EndpointsTests` drives only legacy
`/api/*` through the Gateway against real SQL; per-endpoint unit tests cover only v2
handlers in isolation against EF **in-memory**. Nothing compares the two stacks.

That gap already cost a production-shaped bug: a 2026-06-16 manual Gateway round-trip
found **five list endpoints** (`resources`, `providers`, `locations`, `requests`,
`Requests/Sent`) returning **HTTP 500** from an untranslatable `ORDER BY <valueObject>.Value`
in paged queries. EF in-memory translated `.Value` fine and stayed green; only **real SQL**
surfaced the 500. Fixed by ordering on the value object directly + making
`ResourceName`/`ProviderName`/`LocationName`/typed ids `IComparable`. That regression class
is the canonical thing F7 must catch automatically.

Both stacks run in the same Core API process (discriminator = `api/v2/` route prefix), so a
**live side-by-side** comparison against one seeded database is possible. The existing
`AspireGatewayFixture` already boots the full AppHost topology (SQL, Vault, immudb, Core API,
Gateway), seeds deterministically, exposes per-role dev-auth clients (ADR-0045), and can take
a SQL snapshot for reset (ADR-0052).

## Decisions

### D1 — Live side-by-side, not golden-master snapshots
Each test calls the v1 route and its v2 counterpart on the same seeded DB and compares the
two responses directly. **Rejected:** snapshotting v1 responses (Verify/Snapshooter) — while
both stacks run in parallel a live diff needs no snapshot files to maintain and proves parity
at the moment of cutoff. No snapshot library is introduced.

### D2 — New `Tests/E2E/ParityTests` project; extract the fixture into a shared library
`AspireGatewayFixture` + `seed-data.sql` + helpers move from inside `EndpointsTests` into a
shared E2E fixtures library referenced by both `EndpointsTests` and the new `ParityTests`.
**Rejected:** adding parity tests inside `EndpointsTests` (shares one Aspire boot, no
extraction) — keeping v1-only and cross-stack suites in separate projects is worth the
extraction. **Accepted cost:** each test assembly runs its own xUnit collection and therefore
boots its own AppHost — two Aspire+SQL boots in CI. If CI wall-clock becomes a problem the
fallback is to merge the two into one assembly; the shared fixture makes that reversible.

**Superseded (2026-07-19) by [ADR-0069](0069-testcontainers-only-e2e-retire-aspire-boot.md):**
the fallback above was taken — `ParityTests` and `EndpointsTests` are deleted and their specs
run inside `Tests/E2E/IntegrationTests` on a single Testcontainers-backed topology, no Aspire
boot at all. The rest of this ADR (D1, D3–D5, the declarative parity-spec table) still holds.

### D3 — Reads-first
This phase covers every **GET/list** route pair; writes (create/update/delete/assign) are
Phase 2. **Rationale:** the SQL-translation 500 risk lives in reads (the five regressed
lists); reads have no side effects, so no per-test seed reset is needed; v2 writes were built
test-first and mutate state. Writes get their own later phase using the fixture's
`ResetToSeedAsync`.

### D4 — Declarative parity spec
A table of route pairs — each entry naming the v1 path, the v2 path, the roles to run under,
the shape rule (structural vs must-match-fields), and the **declared intended divergences** —
drives data-driven theories. **Rejected:** per-endpoint normalizer functions — a declarative
table is auditable line-by-line against `v1-retirement-parity.md`, so every tolerated
divergence (value-object envelope, nested route re-shape, GUID→natural-key, 404-hiding) is
visible and reviewable rather than buried in ad-hoc code.

### D5 — Comparison dimensions (deterministic, from the harness + the regression)
The comparison asserts: (a) **HTTP status parity** — directly catches the 500-vs-200 class;
(b) **structural shape** — recursive property-name + JSON-kind sets, modulo declared
divergences, not scalar values (GUIDs/timestamps differ legitimately); (c) **list ordering** —
the exact axis that regressed; (d) **scope semantics** — the same entity set is visible per
role, exercised through the fixture's existing per-role clients. Path is the Gateway with real
SQL — the prod path and the one that caught the original bug.

## Consequences

- **Positive:** an automated, real-SQL, cross-stack gate that would have caught the ORDER-BY
  500; divergences are documented and enforced in one auditable place; the extracted fixture
  is reusable by future E2E suites; unblocks the v1 cutoff decision with data, not memory.
- **Negative / costs:** two Aspire boots in CI (D2); the fixture extraction touches the
  existing green `EndpointsTests` and must be verified not to break it; the current seed may
  need a few more rows so lists have ≥2 elements to prove ordering.
- **Follow-ups (not this ADR):** Phase 2 write-parity (reuses `ResetToSeedAsync`, encodes
  write-only divergences — ProjectTag 409-on-in-use, RoleId route→body); DDI/Service S2S
  read-parity under API-key auth (separate auth dimension).

## Alternatives considered
- **Golden-master snapshots** — rejected (D1): snapshot maintenance with no upside while both
  stacks are live.
- **In-memory EF cross-stack tests** — rejected outright: in-memory is exactly what concealed
  the original regression. Parity must run against real SQL.
- **Parity tests inside `EndpointsTests`** — rejected (D2) for suite separation.
