# F1 implementation plan — `ProjectTag` catalogue delete

> Output of a grilling session, 2026-06-28. Branch `feat/refactor-core`. TDD throughout (red → green per phase).
> Grounds: [CONTEXT.md](../../CONTEXT.md) ("Tag" / "ProjectTag" — a single **global shared catalogue** entry,
> identified by its **name**; *not* a tag scoped to one project), [ADR-0027](../architecture/adr/0027-v2-natural-key-uniqueness-gaps.md)
> (ProjectTag.Name uniqueness gap), [ADR-0024](../architecture/adr/0024-zero-surrogate-v2-contracts.md) (addressed
> by natural key, no GUID in the contract), [next-phase-plan.md](./next-phase-plan.md) (F1).

## Scope

Rebuild the catalogue-level **delete** of a shared `ProjectTag`. Legacy surface:
[ProjectTagEndpoint.cs](../../src/Core/API/API/ProjectTagEndpoint.cs) `PUT ""` (update name+description) and
`DELETE {Id}` (delete by GUID), backed by
[ProjectTagService.cs](../../src/Core/Application/UserOrg/Services/ProjectTagService.cs) `UpdateProjectTagAsync`
/ `DeleteProjectTagAsync`.

v2 already has: `GetProjectTag`/`GetProjectTags` (reads, `ProjectTags/{tagName}`), `CreateProjectTagV2`
(mint-and-assign, `POST Projects/{sn}/Tags`, idempotent via `EnsureTagExistsAsync` + `Project.AssignExistingTag`),
and assign/unassign. **Missing = catalogue delete.** The legacy *rename* + *description* are **dropped** in v2
(see G3) — this phase ships delete only.

The `ProjectTag` aggregate today is a thin entity (`Name`, `CreatedBy`, `IsDeleted`, `Create`) mapped M:N to
`Project` (`_projectTags` via `AssignedProjectTagToProject`); it has a `ProjectTagConfiguration` but no standalone
repository. `GetProjectTags` is **not** output-cached → no cache eviction on delete.

## Decisions taken in the grilling

| # | Question | Decision |
|---|---|---|
| G1 | Aggregate shape for a catalogue-global mutation (blast radius across all projects)? | **Promote `ProjectTag` to an aggregate root** with its own `IRepository<ProjectTag, TagName>` + `Delete()` method (+ error codes). The catalogue entry is independent of any one `Project`, so it is **not** mutated through `Project` (that would breach the aggregate boundary — CONTEXT.md: Tag is global, not per-project). Rejected: extend `IProjectTagHandler` (it is the resolve-or-create port for the M:N *assign*; a standalone catalogue mutation is not an M:N concern). Rejected: mutate via `Project`. |
| G2 | DELETE of a shared tag used by N projects — semantics? | **Block-if-in-use → 409.** If any active `AssignedProjectTagToProject` link references the tag on an active project, refuse with 409; otherwise soft-delete the catalogue entry. **Deliberate divergence from legacy** (legacy deleted unconditionally). Safer: no silent disappearance from live projects. Rejected: unconditional soft-delete (legacy parity, but a shared tag vanishing from all projects via the `Project.Tags` `!t.IsDeleted` filter is surprising); rejected cascade-detach (the read filter already hides a deleted tag, so detaching links is wasted work). |
| G3 | What does "update" cover (legacy set Name + Description; v2 model dropped Description)? | **Drop update entirely.** No rename, no description. v2 `ProjectTag` is **name-only** (CONTEXT.md defines a Tag purely by its name; the modular model already dropped Description). A name change = delete + recreate. **Deliberate divergence from v1 parity** — must be recorded so the F7 parity gate does not flag the missing rename/description as a regression. |
| G4 | Auth for deleting a global catalogue tag? | **Admin-only**, enforced in Core (`caller.Role == roleOptions.Value.DefaultAdminRoleName`, mirroring `CreateProviderEndpoint`). A shared catalogue entry affecting every project is platform maintenance, not a project member's right; with block-if-in-use it only ever removes an *unused* entry. RBAC `perm:projecttag:*` stays operational EndpointMap data (not code), like F4/F5 resource writes. |
| — | ADR-0027 `Tag.Name` filtered unique index + dedup? | **Not in F1.** The data has duplicate tag names and the legacy tag-writer still runs until cutoff, so a unique index would fail to create. Deferred to legacy-writer retirement (ADR-0027). v2's only writer, `EnsureTagExistsAsync`, is resolve-or-create and never mints a duplicate name. |

---

> **Status: ✅ complete (A–D).** Delete-only shipped; the two parity divergences (G2 block-if-in-use, G3
> no rename/description) are recorded in [progress.md](./progress.md) and [next-phase-plan.md](./next-phase-plan.md)
> for the F7 gate. Core.UnitTests green (597 passed, 11 skipped integration).

## Phase A — `ProjectTag` aggregate root + `Delete` (TDD) ✅

1. **Domain:** make `ProjectTag : AggregateRoot` (keep `ISoftDeletableEntity`). Add `public Result<Error> Delete()`
   → soft-delete (`IsDeleted = true`, set `ModifiedAt`); idempotent guard returns a not-found/already-deleted
   error if already deleted (the use-case maps the missing/deleted case to 404 — see Phase C). The **in-use**
   check is cross-aggregate and lives in the use-case (Phase C), not here. Raise no domain event (delete of an
   *unused* tag touches no project; `GetProjectTags` is not cached → nothing to evict).
2. **Errors:** new `ProjectTagErrors` (or extend `ProjectErrors`) + codes `TagNotFound`, `TagInUse`. Map
   `TagInUse → 409`, `TagNotFound → 404` in `DomainErrorResults` (or emit them directly via `Problems` in the
   endpoint — pick whichever the in-use check lands in).
3. **DataAccess:** add `IRepository<ProjectTag, TagName>` implemented on `DatabaseContext` (a `DatabaseContext.ProjectTagRepository.cs`
   partial), `FindAsync(TagName)` querying by `Name` (mirrors `ProviderRepository.FindAsync(ProviderName)` — Guid
   shadow PK, addressed by the natural key). Expose a `ProjectTags` `DbSet`/`Set<ProjectTag>()` as needed; reuse
   the existing `ProjectTagConfiguration`. Register the repository in `Core.DataAccess` DI.

- **TDD / Verify (A):** aggregate unit tests (delete sets `IsDeleted`; double-delete guarded). No DB.

## Phase B — in-use read query (TDD) ✅

1. **New** `IProjectTagQuery.IsTagAssignedToAnyProjectAsync(TagName tagName, CancellationToken ct)` → `bool`:
   `EXISTS` over `AssignedProjectTagToProject` joined to `Project`, true when an active link to the tag exists on
   an active (non-deleted) project. Method-syntax, shadow-FK joins — **use query-syntax `join … equals …`, not
   nested correlated `.Any()` over shadow FKs** (EF in-memory returns empty for the latter — see the F5 Phase C
   lesson). Implement on `ProjectTagDetailsQuery`.

- **TDD / Verify (B):** in-memory query tests (in use / not in use / only a soft-deleted link → not in use) + a
  real-SQL translation guard (per the project's verification rule).

## Phase C — endpoint + auth (TDD) ✅

1. `DeleteProjectTagEndpoint` (`DELETE ProjectTags/{tagName}`; filename/handler/doc per the endpoint conventions;
   `DisabledWhen("ReadOnlyMode")`; group `Admin`).
2. Flow: validate `tagName` (400) → **Admin-only** guard (403) → `repository.FindAsync(tagName)` (404 missing /
   already soft-deleted) → `query.IsTagAssignedToAnyProjectAsync(tagName)` → **409** if in use →
   `tag.Delete()` → `SaveChanges` → **204**. No cache eviction (`GetProjectTags` is uncached).
3. RBAC: route under `perm:projecttag:*` = operational EndpointMap data (same as F4/F5), not code. Map the
   endpoint in `Composition`.

- **TDD / Verify (C):** handler tests — 204 happy path, 404 (missing / already deleted), 409 (in use), 403
  (non-admin), 400 (invalid name). API host builds clean.

## Phase D — docs / trackers ✅

- `progress.md` / `next-phase-plan.md`: F1 done; record the **two parity divergences** (no rename, no
  description; delete blocks-if-in-use) so the F7 parity gate treats them as intended, not regressions.
- This plan's phases marked ✅.

---

## Sequencing & risk

- **Order:** A → B → C → D. Small phase set; B is independent of A (uses `TagName`), C depends on A+B.
- **Low risk** — additive (a new endpoint + a read query + an aggregate-root promotion); no schema change, no
  migration, no data fix. The only judgement calls are the two recorded parity divergences (G2, G3).

## Out of scope (do not fold in)

- **Tag rename / description (G3)** — dropped in v2; not a regression.
- **`Tag.Name` filtered unique index + dedup (ADR-0027)** — gated on legacy-tag-writer retirement; the data has
  duplicates today.
- **The legacy GUID-id route shape** (`DELETE ProjectTag/{Id}`) — v2 addresses by natural key (ADR-0024).
- **Unconditional / cascade delete** — superseded by block-if-in-use (G2).
