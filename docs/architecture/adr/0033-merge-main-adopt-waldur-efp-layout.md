---
status: accepted
---

# Merge of `origin/main` into `feat/refactor-core`: adopt main's Waldur.EFP layout

## Context

`feat/refactor-core` (the v2 modular-Core refactor) had diverged 306 commits from
the merge-base `47604543`; `origin/main` had advanced 111 commits. The two lines
had independently refactored the **same** integration modules, producing a hard
structural collision in `src/Modules/Integrations`:

| `feat/refactor-core`            | `origin/main`                              |
|---------------------------------|--------------------------------------------|
| `Waldur/`, `Waldur.Contracts/`, `Puhuri/` | `Waldur.EFP/`, `Waldur.Puhuri/` (old dirs deleted) |

main migrated Waldur+Puhuri into `Waldur.EFP` + `Waldur.Puhuri` (216/puhuri-integration);
our branch kept refactoring the old layout. The two layouts cannot coexist — a naive
merge leaves both (modify/delete conflicts retain our deleted files alongside main's
new modules) and will not build.

## Decision

1. **Waldur/Puhuri layout: adopt main's `Waldur.EFP` + `Waldur.Puhuri`.** Discard our
   old `Waldur/`, `Waldur.Contracts/`, `Puhuri/` work (main's integration work, incl.
   puhuri #216, is newer and functional). Our refactor adapts to main on the Waldur side.
2. **main's bugfixes (HEAppE reliability, project-requests #211/#212, RBAC/Gateway):
   merge now into legacy `src/Core`** so the tree builds, and record a **port-later list**
   of fixes that must be re-applied in features already migrated to `src/Modules/Core`
   (a textual merge into soon-to-die legacy code does not propagate the fix to the new module).
3. **Execute on a dedicated branch** `merge/main-into-refactor` off `feat/refactor-core`;
   fast-forward `feat/refactor-core` only after build + full test suite are green.

## Consequences

- Our Waldur-side refactor effort is intentionally thrown away; consuming code on our
  branch that referenced the old Waldur namespaces must be repointed to `Waldur.EFP`.
- main's behavioral fixes that land in legacy Core are **not** automatically present in
  the migrated Modules/Core features — tracked separately as porting work.
- Duplicate ADR numbers now exist: main added its own `docs/architecture/adr/0006-0008`
  (single-main-trunk / tagless-rc / per-track-base-images) which differ from ours; main
  also introduced `docs/adr/`. Renumbering is deferred cleanup, not a merge blocker.

## Port-later list (work this merge deferred)

1. **HEAppE liveness-probe reliability into modular Core.** The legacy `Userorg.Adapter/Heappe/HeappeConnectionService`
   took main's version (liveness gate / ADR 0073, dedicated health-probe client, `GetActiveCommandTemplatesAsync`
   fast-fail). The migrated `src/Modules/Integrations/Heappe` code did **not** automatically get this behaviour —
   it must be reflected there. `HeappeVersionRemoteResolver` was hand-merged to keep our `ToNormalizeUrl()` *and*
   main's `HeappeHealthClientName`.
2. **`GetListAvailableClusters` pruning test coverage.** Our canary `GetListAvailableClustersPruningTests` was
   removed: it asserted our old adapter design (adapter fetches `activeCommandTemplates` internally + early-return
   on empty set). Main's design makes `activeCommandTemplates` a caller-provided parameter, so the premise no longer
   holds. Re-establish pruning coverage against main's signature.
3. **`IHeappeConnectionService` name collision.** Two interfaces now coexist — legacy
   `UserOrganizationManagement.Application.UserOrg.IServices.IHeappeConnectionService` (Result-returning,
   `GetListAvailableClusters`) and main's new `Heappe.Contracts.IHeappeConnectionService` (lean, liveness-probe).
   Disambiguated with `using` aliases in `ProjectService`, the legacy adapter, and `ProjectServiceTests`. Pick one
   as the canonical port target and retire the other.
4. **`Heappe.Features.Connection.HeappeConnectionService` Result unwrap.** Its `GetActiveCommandTemplatesAsync`
   now unwraps the Result and throws on failure (interface contract is a bare set). Revisit once the modular Heappe
   contract settles.
5. **Duplicate ADR numbering** (0006–0008) and main's separate `docs/adr/` — renumber/consolidate.
6. **CONTEXT.md vocabulary:** main's request-state terms (`PendingForPIAccept`, `PendingForHPCProject`, `PI`,
   `ProjectResource`/`ProjectResourceAssignment`) were intentionally **not** merged — they clash with our curated
   Requests/Resources vocabulary (which deprecates the `ProjectResource*` code names). Reconcile deliberately if needed.

## Verification at merge time

`dotnet build` green (0 errors). All unit/integration test projects pass. The Aspire **E2E** suite
(`Tests/E2E/EndpointsTests`) was not verifiable in the headless merge session — it builds the full distributed
app model (Vault/SQL containers) and needs a running Docker/Aspire environment. Its AppHost-config logic is
functionally identical to pre-merge `feat/refactor-core`, so those failures are environmental, not merge regressions.
Re-run E2E in a Docker environment before fast-forwarding `feat/refactor-core`.
