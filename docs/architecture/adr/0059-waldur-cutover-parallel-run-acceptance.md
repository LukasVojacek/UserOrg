# Cutover in one branch; acceptance by parallel run and data comparison

**Status:** accepted (grilling session 2026-07-06) · relates to ADR-0053…0058

## Context

Both the old and the new integration are config-gated off by default (`Waldur:Enabled`,
`Puhuri:Enabled` today; `Waldur:Instances[].Enabled` in the new design). Keeping two
implementations side by side in the repo buys little safety for an integration that is verified
against a test Waldur instance anyway, and costs duplicate code and tests plus the risk of both
being enabled at once.

## Decision

**Replace in one feature branch.** The new `Waldur.Contracts` + `Waldur.Sync` + consumer handlers
are built, and the old projects (`WaldurPlatform.Base`, `Waldur.EFP`, `Waldur.Puhuri`, the
exporter side of `Userorg.Adapter/Waldur`) and their test projects are **deleted in the same
branch**. Merge = cutover.

**Acceptance gate before the old code is deleted:** during testing, both implementations are run
(each against its own empty UserOrg database, same test Waldur instance) and the **resulting data
are compared** — the new path must produce an equivalent DB state to the old one for the same
Waldur content (projects, resources, providers, memberships, roles). Differences must be
explained (intentional, e.g. no-op writes eliminated) or fixed.

Old tests are not ported as code; the **behaviours** they pin (scenarios from
`FullPipelineFlowTests`, `AggregateDataValidator` rules, upsert-key semantics from
`WaldurExporterExportDataTests`) are re-expressed as tests of the new components.

## Consequences

- No window where two implementations coexist on main; no dual maintenance.
- The comparison run doubles as the definitive functional-parity check of the whole refactor and
  should be scripted/repeatable, not eyeballed.
- Rollback strategy is git (revert the merge), not a runtime toggle back to the old path.

## Amendments (independent review 2026-07-06)

### A1 — Comparison methodology (a naive dump-diff matches zero rows)

Rows carry `Guid.CreateVersion7()` ids, now()-fallback `CreatedAt`s and `SyncFrom`-written
`ModifiedAt`s, so the comparison script must:

- project each entity to a **natural-key projection** (FKs joined through to external
  ids/names), with an explicit **exclusion list** (surrogate ids, `CreatedAt`, `ModifiedAt`,
  audit users);
- carry a written **expected-differences list**, pre-approved: per-user vs. all-or-nothing
  membership transactions, manual-member sweep timing (map-based vs. DB-based), eliminated no-op
  writes, all-or-nothing-per-project vs. per-resource partial progress, removed projects now
  reaching the DB (`IsDeleted=true`) that the old eligibility skip silently dropped
  (ADR-0054/A1);
- **isolate Heappe**: both runs against the same live HEAppE would double external side effects —
  stub or sandbox HEAppE per run;
- run the **old** side one instance at a time: the old DI (unkeyed singletons for
  `IExporter`/`Channel`/`ICollectorService`) never actually supported concurrent instances.

### A2 — Puhuri baseline is defined by intent, not by today's code

Today's Puhuri path is broken: it is registered with the EFP registration + `Waldur` config
section (copy-paste), writes entities with the **EFP** service name while the user export looks
up the "Puhuri-Waldur" service account (user export can never succeed), and the shared Heappe
initializer hardcodes EFP's PI email. The acceptance comparison for Puhuri therefore validates
the **intended** semantics (service name = instance name, own service account, no storage
reconciliation), and old-vs-new equivalence is only meaningful for EFP.

### A3 — Non-empty-database cutover check

Because of A2, existing production rows written by the buggy Puhuri path carry EFP service names.
On a **non-empty** DB the new (correct) upsert keys would not match those rows and would create
duplicates — the empty-DB acceptance run cannot catch this. Phase 5 includes a data check
(count rows whose ServiceName does not match their true instance) and, if hits exist, a
one-time data fix decided with the team before enabling the new sync.

### A4 — First-run storm is expected and harmless

The first new-sync run against a DB populated by the old implementation may emit a one-time wave
of `ProjectUpdated`/`ProviderUpdated` events wherever mapping nuances (truncation points,
normalization) differ. With Heappe ensure being idempotent get-or-create-or-update
(ADR-0057/A1), this is noise, not damage. Expect it; do not panic on cutover day.

