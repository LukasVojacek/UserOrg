---
status: accepted
---

# Merge of `origin/main` into `feat/refactor-core`, round 2: repair a GitLab-squashed lineage

## Context

`af9cf555 "chore: merge origin/main into feat/refactor-core"` — the commit that recorded the
previous round's decision (ADR-0033) — has a **single parent**. It is a GitLab **squash-merge**
of MR !332 (`merge_method: ff`, `squash_option: always` on this project), not the merge commit
ADR-0033 called for. Squashing collapses the two-parent history into one, so git no longer
knows `7bf13da2` (main's tip at that time) was ever merged:

- `git merge-base HEAD origin/main` resolved to the stale `a7292a16`, `origin/main`'s tip
  *before* ADR-0033's merge, instead of `7bf13da2`.
- A naive `git merge origin/main` against that stale base re-diffs this branch's entire
  independent history since `a7292a16` against main's, producing 108 conflicts (48 after
  renormalization) — almost all already resolved once by ADR-0033.
- `state/research/squash-lineage.md` exhaustively diffed every file main touched between
  `a7292a16` and `7bf13da2` (70 files) against this branch's tree: zero cases of main content
  present at `7bf13da2` and missing here (mechanical ADR-renumbering and CRLF aside). HEAD is a
  verified strict content superset of `7bf13da2` — the squash lost lineage, not content.

## Decision

1. **Repair the lineage before merging the delta.** `git merge -s ours 7bf13da2` records
   `7bf13da2` as merged without changing the tree (verified empty `git diff HEAD@{1} HEAD`).
   This moves the effective merge-base for the next `git merge origin/main` from the stale
   `a7292a16` to `7bf13da2`, collapsing the conflict surface to the true 8-commit delta
   (`5e2bb725 40edb409 de737bab d7e1db30 c61156b3 ce9e3bcd 2b546968 4b3d785b`) instead of a naive
   merge against the stale base, which re-diffs this branch's entire independent history since
   `a7292a16` against main's current tip (108 conflicts, 48 renormalized — almost all already
   resolved once by ADR-0033). `git merge-base HEAD origin/main` now reports `5e2bb725` — main's
   actual tip at merge time — confirming the repair.

2. **Discard main's legacy migration `20260728125807_DropGuidIdDefaults`.** It targets the
   legacy `UserOrganizationManagement.Infrastructure.DataAccess.Contexts.DatabaseContext`, which
   appears in **neither** of `src/Tools/DatabaseMigrations/MigrationRunner.cs`'s two hardcoded
   context lists (`:203-208` apply, `:714-719` script/CI) — it is not a migration owner and never
   was; the migration would never apply. Accepting the rename git proposes for it would let a
   dead file ride into the modular canonical migration chain next to `ModularCleanup`, which
   already made this exact schema change.

3. **Merge with `-c merge.renames=false`, not `directoryRenames=false`.** Git's default rename
   detection maps main's edit to the legacy `DatabaseContextModelSnapshot.cs` onto the frozen,
   already-applied modular migration snapshot
   `src/Modules/Core.DataAccess/Migrations/20260627143515_AddProjectResourceProviderId.Designer.cs`
   (a `R097` per-file match against `20260715111657_SyncPendingModelChanges.Designer.cs`) and
   **auto-merges it without a conflict**, silently dropping `HasDefaultValueSql("NewId()")` from
   every Guid PK in that already-shipped migration. This is a per-file match, not a directory
   heuristic: measured with `-c merge.directoryRenames=false` alone — per-file rename detection
   still on — the frozen file is **still** rewritten by 56 lines (27 conflicts in that
   configuration). Only disabling per-file rename detection entirely (`merge.renames=false`, 33
   conflicts) leaves the modular migrations directory untouched. The extra conflicts this trades in are real and were resolved
   by hand; the silent rewrite it prevents would have gone undetected until a runtime
   `SqlException 515` on the first `INSERT` against a PK whose default the merge quietly deleted.

4. **The 14 legacy `EntityConfigurations` keep this branch's `ApplyCodeGeneratedGuidKeys()`
   convention (ADR-0061), not main's `.ValueGeneratedNever()`.** Both sides removed the same
   `HasDefaultValueSql(GuidIdSql)` line — main because it moved to store-generation-off with an
   explicit ctor-assigned `Id`, this branch because ADR-0061 already made the legacy context
   client-generate every Guid PK via the shared `UuidV7ValueGenerator` at `ValueGenerated.OnAdd`.
   Taking main's `.ValueGeneratedNever()` would silence the generator: for `ProjectResource`,
   `Provider`, and `Location`, whose constructors mint no `Id` in main's `7bf13da2` state, that
   would insert `Guid.Empty`. Legacy V1 Core is registered unconditionally
   (`src/Core/API/Program.cs:116,171`), independent of `V2Api` — a live production write path,
   not test-only surface.

5. **Decision 4's original rationale goes stale, harmlessly, and separately exposes a
   pre-existing defect the merge widens.** By merge time, main's own later commits had *already*
   added ctor-side `Id` assignment to `ProjectResource`/`Provider`/`Location` (auto-merges
   cleanly, since this branch never touched those constructors), so "these three entities have
   no ctor assignment" is no longer true post-merge. That much is inert — the `OnAdd` convention
   is still the source of truth, and a ctor mint EF never has to consult reads as a redundant
   belt-and-braces assignment. **What is not inert is the combination of a ctor-minted `Id` with
   `ValueGenerated.OnAdd` on the tracked-entity path.** B01 found
   `ProjectUserPermissionTrackingTests` (main's regression guard for issue #280) red post-merge:
   it expects a `UserPermission` discovered through a tracked `Project`'s navigation to be
   painted `Added`, and gets `Modified`. EF's `NavigationFixer` paints `Modified` when the key is
   *both* set *and* store-generated; with `OnAdd` **and** a non-empty ctor-minted `Id` both
   conditions hold, so the change-tracker treats the entity as pre-existing and emits an `UPDATE`
   that matches no row — `DbUpdateConcurrencyException`, the exact failure #280 was about. On
   main the test passes only because main also carries `.ValueGeneratedNever()`, the other exit
   from that branch, which this branch will not adopt (decision 4 above).

   Two attributions must be stated precisely, because the obvious reading of both is wrong:

   - **The merge did not create this trap; it widened it.** `UserPermission` — the entity the red
     test uses — already carried `public UserPermission() => Id = Guid.CreateVersion7();` at this
     branch's pre-merge tip `3b3809f7`, as did `User`, `Project`, `Request` and `ActionLink`.
     Those five ctor mints are `feat/refactor-core`'s own and pre-date the merge, so the defect
     was live here before B01 ran; what the merge imported was main's *guard test*, which exposed
     it. The merge did add nine further mints (`AssignedLocation`, `AssignedLocationValue`,
     `Configuration`, `Location`, `ProjectResource`, `ProjectTag`, `Provider`, `UserPreference`,
     `UserProjectAccess`), taking the trap from 5 entities to 14.
   - **Legacy `POST /api/Project` does not actually 409 today.** Both `UserPermission` add-sites
     in `ProjectService` (`:231-238`, `:438-445`) pair the navigation add with an explicit
     `UserPermissionRepository.InsertAsync`, and an explicit `Add` paints `Added` regardless of
     the key — so the live route is already masked at both known call sites. The failure is
     model-level, and the guard test states the invariant those `InsertAsync` calls compensate
     for; it protects every future navigation add, not a route that is broken right now.

   **Decision 19's "no impact" reading is revoked.** The fix stays inside decision 4 — `OnAdd`
   remains the source of truth — and is B03's, by not minting an `Id` in the ctor of legacy
   entities the shared convention already covers. The constraint this ADR fixes in place:
   **`.ValueGeneratedNever()` must not be reintroduced** on any legacy `DatabaseContext` Guid PK,
   on this path or any other, regardless of how the ctor side of the fix lands.

6. **Integrate by direct push to `feat/refactor-core`, not by merge request.** This project's
   `merge_method: ff` plus `squash_option: always` means an MR can never produce a merge commit
   — that combination is exactly the mechanism that squashed `af9cf555` and destroyed the
   lineage this ADR repairs. Routing the fix back through an MR would reproduce the defect it
   exists to fix. `feat/refactor-core` is not a protected branch (`push: No one` protects only
   `main`; `push_rule` is 404 on this CE instance; no CODEOWNERS). Condition: since the branch
   pipeline runs only after the push lands, every done-criterion in `plan-ready.md` §4 must be
   green locally first.

## Consequences

- The lineage is repaired for every future merge from `origin/main`: `git merge-base` now
  tracks main's real tip instead of drifting further behind at each squash.
- The legacy V1 write path keeps generating Guid PKs client-side; main's store-generation-off
  design does not apply to it. Divergence is intentional and documented in ADR-0061, not a gap.
- The interaction in decision 5 above (ctor-minting + shared `OnAdd`) is a defect that pre-dates
  this merge on five entities and is widened by it to fourteen; tracked to B03, not fixed by
  this ADR.
- Direct-push integration means the branch pipeline is a post-hoc check, not a gate — all ten
  `plan-ready.md` §4 criteria must be verified locally before the push.

## Binding procedure for the next integration merge

Decision 6 above is this round's instance of the rule; this section makes it binding for every
future `main` → `feat/refactor-core` integration, because the root cause — `squash_option: always`
— is untouched and will recur. Verify the two settings yourself rather than trusting this
paragraph: `glab api projects/lexis-platform%2Fbackend%2Fuserorg` returns both `merge_method: ff`
and `squash_option: always`.

1. **Ordinary feature work goes through MRs; squash there is fine and desirable.** The constraint
   below is specific to integrating `origin/main` into `feat/refactor-core`, not to MRs in general.
2. **An integration merge of `main` into `feat/refactor-core` is always a direct push, never an
   MR.** `merge_method: ff` + `squash_option: always` means no MR on this project can ever produce
   a merge commit — routing the integration through one collapses its two parents into one and
   reproduces exactly the damage this ADR repairs.
3. **Recognise a destroyed lineage by its fingerprint**: a commit whose message reads like
   `chore: merge X into Y` but has only one parent.
   ```
   git log -1 --format='%H %P %s' <sha>
   ```
   One hash after the commit hash in `%P` (instead of two) is the tell.
4. **Repair it the way this round did.** First resolve `<last-integrated-main-sha>` — the
   `origin/main` tip the squashed commit absorbed. Nothing records it (ADR-0033 does not name this
   round's `7bf13da2`), so derive it from the squashed commit's own date, which reproduces
   `7bf13da2` here:
   ```
   git log -1 --format='%H' --before="$(git log -1 --format=%cI <squashed-sha>)" origin/main
   ```
   Confirm this branch already contains that commit's content before using it: `-s ours` keeps this
   branch's tree verbatim, so anything present there and missing here is dropped silently and no
   later merge will ever bring it back — this round proved the superset file by file first (Context
   above). Then:
   ```
   git merge -s ours <last-integrated-main-sha>
   git -c merge.renormalize=true -c merge.renames=false merge origin/main
   ```
   The first re-records the absorbed history without changing the tree; the second brings in the
   real delta. Both flags matter: `renormalize=true` collapses the line-ending noise (Context above:
   108 conflicts, 48 after renormalization), and decision 3 explains why rename detection must be
   disabled with `merge.renames=false` and not `directoryRenames=false`.
5. **The direct push is possible because `feat/refactor-core` is unprotected** (only `main` is
   protected on this project). If that ever changes, this procedure needs revisiting before the
   next integration merge.

## Port-later list (reverse direction: a future merge of this branch back into `main`)

`-s ours 7bf13da2` plus every `ours` conflict resolution in this merge make `feat/refactor-core`
the *newer* side for every file it touched. A future merge in the **opposite** direction — this
branch back into `main` — would silently **delete from `main`**, with no conflict to flag it:

1. **`main`'s live legacy migration `20260728125807_DropGuidIdDefaults`.** Decision 2 above
   discards it on this branch because the legacy context is not a migration owner *here*. On
   `main` it is: `origin/main:src/Tools/DatabaseMigrations/MigrationRunner.cs:206,714` still
   lists the legacy context in both apply and script lists. Decision 2's rationale is
   **branch-local** — do not port the discard onto `main`.
2. **`main`'s `.ValueGeneratedNever()` configurations** on the 14 legacy `EntityConfigurations`
   — decision 4 replaces them with `ApplyCodeGeneratedGuidKeys()` here; a reverse merge would
   carry that replacement onto `main`, where the store-generated default this ADR's decision 2
   confirms is dead (`DropGuidIdDefaults`) is what makes `.ValueGeneratedNever()` correct there.
3. **`main`'s `docs/architecture/adr/0009-client-generated-guid-v7-identifiers.md`** — deleted
   here as a duplicate of ADR-0061 (same decision, this branch's number). It is not a duplicate
   on `main`, where ADR-0061 does not exist.

Whoever performs that future merge must resolve these three paths toward `main`'s content, not
this branch's, regardless of which side git's default heuristic picks.

## Verification at merge time

`dotnet build` green (0 errors, pre-existing warning debt unchanged). `git status --porcelain`
empty, no conflict markers, `git diff --check` empty. `git merge-base HEAD origin/main` reports
`5e2bb725`. `git diff <pre-merge-HEAD> HEAD` over both migrations directories shows only the
decision-2 deletions — no previously-applied migration's content changed. Full criteria list
(build, test suite, E2E parity, migrate-from-zero, OpenAPI snapshots, `has-pending-model-changes`,
`dotnet format`, upgrade-from-a-main-migrated-DB) is `plan-ready.md` §4; satisfied incrementally
across this block and B02/B03/B05, not by this ADR alone.
