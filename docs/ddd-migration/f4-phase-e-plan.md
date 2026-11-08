# F4 Phase E implementation plan — ADR-0030 + trackers (close-out)

> Closes F4 (ProjectResource CRUD write model). Phases A–D are **done**; this phase is **docs-only** — no
> code, no tests. All decisions are already settled (grilling tables D1–D6, C1–C4 in
> [f4-projectresource-crud-plan.md](./f4-projectresource-crud-plan.md)); Phase E only transcribes them into a
> standing ADR and flips the trackers.
>
> Branch `feat/refactor-core`. Grounds: [ADR-0026](../architecture/adr/0026-projectresource-per-provider-identity.md),
> [ADR-0027](../architecture/adr/0027-v2-natural-key-uniqueness-gaps.md),
> [ADR-0028](../architecture/adr/0028-transactional-outbox-for-domain-events.md),
> [ADR-0029](../architecture/adr/0029-natural-keys-at-contract-and-aggregate-boundaries.md).

## Why an ADR (not just the plan file)

Two hard-to-reverse calls were made *during* implementation that **diverge from ADR-0026's stated wording**,
plus the create-contract diverges from legacy. Those belong in a standing ADR, not buried in a migration
plan. ADR-0030 is the record; it **refines/amends ADR-0026** on the two enforcement-seam points below.

| ADR-0026 said | What shipped (A–D) | Why |
|---|---|---|
| Per-provider name uniqueness enforced "in the use-case **via a domain port**" + integration test | A **read query** `GetResourceCreateContextAsync` (no port); filtered unique index = concurrency backstop | A pure *create* has no resolve-then-mutate of an existing aggregate, so a port adds nothing over a read (mirrors `CreateProvider`'s `ProviderExistsAsync`). The resolve-then-mutate port pattern still applies where it's real (F5). |
| Invariant #5 enforced "**inside the aggregate method**" (`AssignResource` compares the resource's and location's `ProviderId`) | Invariant #5 = **per-provider resolution in the use-case** (D1/D6): resolve `(providerName, resourceName)`; a foreign/unknown resource is simply not found → 404 | `Provider` has no CLR surrogate id (keyed by `ProviderName`); an in-aggregate `ProviderId` compare would force surfacing the shadow id for no extra safety. The per-provider resolver *is* the invariant boundary. |

---

## Step 1 — `docs/architecture/adr/0030-projectresource-write-model.md`

Title: **ADR 0030 — ProjectResource write model: aggregate root, immutable per-provider identity, and the
invariant-#5 enforcement seam.**

Header block (match the ADR-0026/0029 style):
- **Date:** 2026-06-28.
- **Status:** accepted (2026-06-28) — formalises decisions made while implementing F4 (Phases A–D); live on
  `feat/refactor-core`.
- **Refines / amends:** ADR-0026 on the two enforcement-seam points in the table above (uniqueness seam =
  read query, not domain port; invariant #5 = use-case per-provider resolution, not in-aggregate compare).
- **Relates to:** ADR-0016 (resource-assignment writes through Provider), ADR-0024 (zero-surrogate v2
  contracts), ADR-0027 (natural-key uniqueness gaps), ADR-0028 (outbox — F5/F6 prerequisite), ADR-0029
  (natural keys at boundaries).

### Decision (the body must record)

1. **Aggregate-root promotion.** `ProjectResource : AggregateRoot` (was `BaseAuditableEntity`), keeping
   `ISoftDeletableEntity, IServiceIntegrationEntity, IHasValidityPeriod`. Owns its lifecycle: `Create`,
   `UpdateDetails`, `Delete`. `Id`/`CreatedAt`/`ModifiedAt` are EF shadow properties.
2. **`ProviderId` ownership (nullable).** Identity is `(ProviderId, Name)` (ADR-0026). The CLR property is
   nullable **only** to materialise legacy standalone rows; a v2-created resource always carries one.
3. **Immutable name / rename disabled (D2).** PUT touches metadata only (`Description?`,
   `PrimaryInvestigatorEmail`, `StartDate`, `EndDate`); `operationMode`/`externalId`/`serviceName` are
   **preserved** from the loaded aggregate (C2). A rename is delete + recreate — no second uniqueness path.
4. **Per-provider uniqueness via a create-context read query + index backstop (refines ADR-0026).**
   `GetResourceCreateContextAsync(providerName, name)` → `(ProviderId, NameTaken)?`: null → 404 (no active
   provider), `NameTaken` → 409. Surrogate `ProviderId` resolved server-side (ADR-0029).
   `IX_ProjectResource_ProviderId_Name WHERE IsDeleted = 0` is the **concurrency backstop** for the
   check-then-insert race, not the primary guard.
5. **Invariant #5 enforced by per-provider resolution in the use-case (D1, amends ADR-0026).** PUT/DELETE
   resolve `(providerName, resourceName)` → `ProjectResourceId?` via `GetResourceIdAsync` (filters
   `IsDeleted = 0`); a foreign/unknown/deleted resource resolves to null → 404 (C1/C3). The **assign** path
   (D3/D6) resolves the same way through `GetResourceForAssignmentByNameAsync(providerName, resourceName)` —
   one method-syntax read query with an `EXISTS`/`Any` provider-ownership gate (the non-null-`==`-nullable
   `ProviderId` comparison, no inner `.Join`). A name under another provider, or a NULL-`ProviderId` legacy
   row, never matches → the existing `provider.resource_not_found` 404.
6. **Create-contract divergence from legacy (record explicitly).** v2 create is **provider-nested**
   (`POST Providers/{providerName}/Resources`), **stamps `ProviderId`** from the route, and **drops** legacy's
   flat provider-less standalone creation. Body is the faithful legacy set
   (`{ Name, Description?, PrimaryInvestigatorEmail, StartDate, EndDate }`); `PrimaryInvestigatorEmail` is
   required (legacy parity).
7. **D5 — NULL-`ProviderId` legacy rows are unreachable via v2 provider-nested assign. Accepted.** A
   provider-less resource has no provider to address it under. The 0026 split backfilled every
   allocation-bearing resource; the residual NULL population = zero-allocation legacy rows (no `Location` ⇒ no
   derivable provider), already documented as the ADR-0026 edge (its Consequences "Edge — zero-allocation /
   standalone resources", lines 77–80). Bounded by F1/F3 legacy-writer retirement, **out of F4 scope**.
   Cross-link that edge rather than claiming "all legacy rows fixed". The
   `ProviderId == route || ProviderId == null` alternative is **rejected** — it resurrects the cross-provider
   ambiguity D3 exists to kill and leaks ownership.

### Endpoint surface (record the table)

| Verb | Route | Name | Success | Auth |
|---|---|---|---|---|
| POST | `Providers/{providerName}/Resources` | `CreateResourceV2` | 201 (`CreatedAtRoute` → `GetResourceByNameV2`) | ProviderAdmin + `ProviderWriteAuthorization` |
| PUT | `Providers/{providerName}/Resources/{resourceName}` | `UpdateResourceV2` | 204 | ProviderAdmin + `ProviderWriteAuthorization` |
| DELETE | `Providers/{providerName}/Resources/{resourceName}` | `DeleteResourceV2` | 204 | ProviderAdmin + `ProviderWriteAuthorization` |

All three `DisabledWhen("ReadOnlyMode")`. Errors: 400 (validation, before any load) · 403 (wrong provider
identity) · 404 (unknown provider / resource / cross-provider / soft-deleted) · 409 (create: name taken
under provider).

### Considered-options / Consequences (keep short)

- **Options** to record per the seam table: domain port vs read query (chose read query for the pure-create
  case); in-aggregate `ProviderId` compare vs use-case resolution (chose use-case — no CLR provider id).
- **Consequences:** F5 (project↔resource assignment) unblocked — it resolves resources by name through the
  same per-provider seam. The overlay-migration snapshot debt (whole-model drift) is **pre-existing and
  out of scope** (see plan "Migrations-overlay debt"). `ProviderId` NOT-NULL tightening stays deferred
  (legacy NULL rows persist by design).

---

## Step 2 — `docs/ddd-migration/progress.md`

- Status snapshot row "Resource per-provider (ADR-0026)": → **F4 complete (A–E); ADR-0030 recorded**.
- F4 section header: `Phases: A ✅ · B ✅ · C ✅ · D ✅ · E ✅`.
- Add a Phase D bullet (currently missing — only A/B/C are listed): per-provider assign resolution
  (`GetResourceForAssignmentByNameAsync(providerName, …)` with `EXISTS` ownership gate), suite **552 / 0 / 9**,
  API host clean.
- Add a Phase E bullet: ADR-0030 recorded; trackers flipped.
- **Delete the stale legacy F4 checklist** (the unchecked `[ ]` block, progress.md lines ~76–82:
  "Promote … aggregate root", "Aggregate-first guards", "CRUD endpoints", "Deploy debt", "Overlay sync") —
  superseded by the A–E bullets above; leaving it reads as unfinished work.
- F5 line: note it is now **unblocked**.
- **Deferred (one line, per plan):** `GetResourceIdAsync` does a redundant `providerName → ProviderId`
  round-trip (binary outcome — could be a single `EXISTS`-gated query like D6); tooling debt, not a
  correctness risk, not folded into F4.
- Bump the "Last updated" date to 2026-06-28; refresh the Tests count to 552 / 0 / 9.

## Step 3 — `docs/ddd-migration/next-phase-plan.md`

- Phase table (line 18) F4 row: append **✅ hotovo** (matching the F0 row style).
- F4 section (lines 70–75): add a one-line "✅ hotovo — viz ADR-0030 + f4-projectresource-crud-plan.md" and
  resolve its open note ("zvážit ADR o write modelu" → ADR-0030 written).
- "Otevřené / rizika" ADR-0026 bullet (line 99): note the enforcement seam landed as **read query +
  use-case resolution** (ADR-0030 refines the "domain port / in-aggregate" wording).

## Step 4 — `docs/ddd-migration/f4-projectresource-crud-plan.md`

- Mark the Phase E section (lines 218–234) **✅ done** in the same style as A/B/D, pointing at ADR-0030.

---

## Verify (E)

- ADR-0030 exists; its cross-links (ADR-0016/0024/0026/0027/0028/0029) resolve; the two "refines ADR-0026"
  points are stated explicitly.
- `progress.md` / `next-phase-plan.md` / `f4-…-plan.md` show F4 A–E ✅; no stale unchecked F4 items remain;
  F5 marked unblocked; the `GetResourceIdAsync` deferral noted once.
- No code touched → no build/test needed (full suite already **552 / 0 / 9**, API host clean from Phase D).
- `git status` shows only the four doc files + the new ADR.

## Out of scope (do not fold in)

- Overlay-migration snapshot regeneration (whole-model drift; own task).
- `ProviderId` NOT-NULL tightening (ADR-0026 — legacy NULL rows by design).
- The `GetResourceIdAsync` single-query optimisation (noted as deferred, not done).
- F5 design/implementation (next task).
